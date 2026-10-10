#!/usr/bin/env bash
# Restore the latest backup into a throwaway PostgreSQL and check it (#15).
# Run monthly by .github/workflows/restore-test.yml, or by hand (docs/runbook.md).
#
# Environment:
#   BACKUP_S3_ENDPOINT BACKUP_S3_BUCKET BACKUP_S3_ACCESS_KEY BACKUP_S3_SECRET_KEY
#   BACKUP_AGE_KEY_FILE  age identity file (the private key; never on the node)
#   BACKUP_PG_IMAGE      default postgres:17-alpine (the dump's major version or newer)
#   BACKUP_NAME          a specific dump under db/; default: the newest
#   KEEP=1               leave the database running on 127.0.0.1:${RESTORE_PORT:-55433}
#   RESTORE_TARGET_URL   after the check, also restore into this empty database
#                        (postgresql://..., the direct URI of a new Neon branch)
set -euo pipefail

: "${BACKUP_S3_ENDPOINT:?}" "${BACKUP_S3_BUCKET:?}" "${BACKUP_S3_ACCESS_KEY:?}" "${BACKUP_S3_SECRET_KEY:?}"
: "${BACKUP_AGE_KEY_FILE:?}"
pg_image=${BACKUP_PG_IMAGE:-postgres:17-alpine}
rclone_image=rclone/rclone:1.71.1
port=${RESTORE_PORT:-55433}
container=tc-restore-test-$$

log() { printf '[restore] %s\n' "$*"; }

export RCLONE_CONFIG_S3_TYPE=s3 RCLONE_CONFIG_S3_PROVIDER=Other RCLONE_CONFIG_S3_ENDPOINT=$BACKUP_S3_ENDPOINT
export RCLONE_CONFIG_S3_ACCESS_KEY_ID=$BACKUP_S3_ACCESS_KEY RCLONE_CONFIG_S3_SECRET_ACCESS_KEY=$BACKUP_S3_SECRET_KEY
work=$(mktemp -d "${RESTORE_WORK_DIR:-$PWD}/.restore.XXXXXX")
cleanup() {
    rm -rf "$work"
    [[ ${KEEP:-} == 1 ]] || docker rm -f "$container" >/dev/null 2>&1 || true
}
trap cleanup EXIT

rclone() {
    docker run --rm -e RCLONE_CONFIG_S3_TYPE -e RCLONE_CONFIG_S3_PROVIDER -e RCLONE_CONFIG_S3_ENDPOINT \
        -e RCLONE_CONFIG_S3_ACCESS_KEY_ID -e RCLONE_CONFIG_S3_SECRET_ACCESS_KEY \
        -v "$work:/restore" "$rclone_image" "$@" 2> >(grep -v 'rclone.conf" not found' >&2)
}

name=${BACKUP_NAME:-$(rclone lsf "s3:$BACKUP_S3_BUCKET/db/" | grep '\.dump\.age$' | sort | tail -n 1)}
[[ -n $name ]] || { log "no backup found in s3:$BACKUP_S3_BUCKET/db/"; exit 1; }
log "restoring $name"
rclone copyto "s3:$BACKUP_S3_BUCKET/db/$name" "/restore/$name"
age --decrypt --identity "$BACKUP_AGE_KEY_FILE" --output "$work/backup.dump" "$work/$name"

log "starting a throwaway PostgreSQL ($pg_image)"
docker run -d --name "$container" -e POSTGRES_PASSWORD=restore -e POSTGRES_DB=restore \
    -p "127.0.0.1:$port:5432" -v "$work:/restore:ro" "$pg_image" >/dev/null
for _ in $(seq 1 60); do
    docker exec "$container" pg_isready -U postgres -d restore >/dev/null 2>&1 && break
    sleep 1
done
docker exec "$container" pg_restore --exit-on-error --no-owner --no-privileges -U postgres -d restore /restore/backup.dump

sql() { docker exec "$container" psql -U postgres -d restore -tAc "$1"; }
migrations=$(sql 'SELECT count(*) FROM __ef_migrations_history')
[[ $migrations -gt 0 ]] || { log "no migrations recorded: the dump is not a tc-dashboard database"; exit 1; }
log "migrations: $migrations (latest $(sql 'SELECT max(migration_id) FROM __ef_migrations_history'))"
for table in users tenants memberships stations broadcast_credentials audit_entries; do
    log "$table: $(sql "SELECT count(*) FROM $table") rows"
done
log "ok: $name restores"
if [[ -n ${RESTORE_TARGET_URL:-} ]]; then
    log "restoring into the target database"
    PGURL=$RESTORE_TARGET_URL docker run --rm -e PGURL -v "$work:/restore:ro" "$pg_image" \
        sh -c 'exec pg_restore --exit-on-error --no-owner --no-privileges -d "$PGURL" /restore/backup.dump'
    log "target restored"
fi
if [[ ${KEEP:-} == 1 ]]; then
    log "kept: postgresql://postgres:restore@127.0.0.1:$port/restore (docker rm -f $container when done)"
fi
