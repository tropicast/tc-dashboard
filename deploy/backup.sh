#!/usr/bin/env bash
# Nightly database backup (#15), run by tc-dashboard-backup.timer as deploy.
#
# pg_dump (custom format) of the direct database URL, encrypted with age to
# BACKUP_AGE_RECIPIENT (the private key stays offline), uploaded to
# s3://$BACKUP_S3_BUCKET/db/. Dumps older than BACKUP_RETENTION_DAYS (30) are
# deleted. Restore: docs/runbook.md.
#
# Settings come from $DEPLOY_ROOT/.env (written by the deploy workflow):
#   DATABASE_DIRECT_URL  postgresql://... (not the pooler)
#   BACKUP_S3_ENDPOINT BACKUP_S3_BUCKET BACKUP_S3_ACCESS_KEY BACKUP_S3_SECRET_KEY
#   BACKUP_AGE_RECIPIENT
#   BACKUP_PG_IMAGE (default postgres:17-alpine: match the server's major version)
set -euo pipefail

root=${DEPLOY_ROOT:-/opt/tc-dashboard}
rclone_image=rclone/rclone:1.71.1

log() { printf '[backup] %s\n' "$*"; }

# Read one setting without sourcing the file. The deploy workflow writes
# every value in single quotes (literal for Compose; no $ expansion).
setting() {
    local value
    value=$(grep -m1 "^$1=" "$root/.env" | cut -d= -f2- || true)
    value=${value#\'}
    value=${value%\'}
    [[ -n $value || -n ${2+x} ]] || { log "missing $1 in $root/.env"; exit 1; }
    printf '%s' "${value:-${2:-}}"
}

PGURL=$(setting DATABASE_DIRECT_URL)
recipient=$(setting BACKUP_AGE_RECIPIENT)
bucket=$(setting BACKUP_S3_BUCKET)
retention=$(setting BACKUP_RETENTION_DAYS 30)
pg_image=$(setting BACKUP_PG_IMAGE postgres:17-alpine)
RCLONE_CONFIG_S3_TYPE=s3
RCLONE_CONFIG_S3_PROVIDER=Other
RCLONE_CONFIG_S3_ENDPOINT=$(setting BACKUP_S3_ENDPOINT)
RCLONE_CONFIG_S3_ACCESS_KEY_ID=$(setting BACKUP_S3_ACCESS_KEY)
RCLONE_CONFIG_S3_SECRET_ACCESS_KEY=$(setting BACKUP_S3_SECRET_KEY)
export PGURL RCLONE_CONFIG_S3_TYPE RCLONE_CONFIG_S3_PROVIDER RCLONE_CONFIG_S3_ENDPOINT \
    RCLONE_CONFIG_S3_ACCESS_KEY_ID RCLONE_CONFIG_S3_SECRET_ACCESS_KEY
[[ $retention =~ ^[0-9]+$ ]] || { log "BACKUP_RETENTION_DAYS must be a number"; exit 1; }

# Under the deploy root, not /tmp: the dump can be larger than a tmpfs.
work=$(mktemp -d "$root/.backup.XXXXXX")
trap 'rm -rf "$work"' EXIT
name="tropicast-$(date -u +%Y%m%dT%H%M%SZ).dump.age"

log "dumping"
# Secrets pass as environment variables, never on a command line.
docker run --rm -e PGURL "$pg_image" sh -c 'exec pg_dump --format=custom --no-owner --no-privileges "$PGURL"' |
    age --encrypt --recipient "$recipient" > "$work/$name"
size=$(stat -c %s "$work/$name")
[[ $size -gt 1024 ]] || { log "the dump is suspiciously small ($size bytes)"; exit 1; }

rclone() {
    docker run --rm -e RCLONE_CONFIG_S3_TYPE -e RCLONE_CONFIG_S3_PROVIDER -e RCLONE_CONFIG_S3_ENDPOINT \
        -e RCLONE_CONFIG_S3_ACCESS_KEY_ID -e RCLONE_CONFIG_S3_SECRET_ACCESS_KEY \
        -v "$work:/backup:ro" "$rclone_image" "$@"
}

log "uploading $name ($size bytes)"
rclone copyto "/backup/$name" "s3:$bucket/db/$name"
log "deleting dumps older than $retention days"
rclone delete --min-age "${retention}d" "s3:$bucket/db/"
log "done"
