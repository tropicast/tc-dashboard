#!/usr/bin/env bash
# Activate a release on the control-plane node (#15). Runs as the deploy user.
#
#   deploy.sh activate <bundle-dir> <git-sha>   migrate, swap, health check (rolls back on failure)
#   deploy.sh rollback                          previous release, no migration
#   deploy.sh status
#   deploy.sh compose <args>                    docker compose with the active release's tag
#
# Layout under $DEPLOY_ROOT (default /opt/tc-dashboard):
#   .env              settings and secrets, written by the deploy workflow (0600)
#   compose.yaml      active Compose file
#   caddy/Caddyfile   active Caddy config (directory-mounted)
#   backup.sh         nightly backup (systemd timer from infra/ansible)
#   releases/<sha>/   copy of each deployed bundle; release.env holds DASHBOARD_IMAGE_TAG
#   CURRENT, PREVIOUS "<git-sha> <image-tag>" of the active and prior release
#
# Registry login for the pull is done by the caller.
set -euo pipefail

root=${DEPLOY_ROOT:-/opt/tc-dashboard}
keep_releases=5
health_url=${HEALTH_URL:-http://127.0.0.1:8080/health}
cd "$root"

compose() {
    docker compose --project-directory "$root" -f "$root/compose.yaml" --env-file "$root/.env" "$@"
}

log() { printf '[deploy] %s\n' "$*"; }

install_bundle() {
    local bundle=$1
    install -d -m 0750 caddy
    install -m 0640 "$bundle/compose.yaml" compose.yaml
    # Overwrite in place: some bind-mount setups do not show a file that was
    # replaced by rename to the running container.
    cat "$bundle/Caddyfile" > caddy/Caddyfile
    chmod 0644 caddy/Caddyfile
    install -m 0750 "$bundle/backup.sh" backup.sh
    install -m 0750 "$bundle/deploy.sh" deploy.sh.new
    mv deploy.sh.new deploy.sh
}

load_release() {
    local sha=$1
    DASHBOARD_IMAGE_TAG=$(sed -n 's/^DASHBOARD_IMAGE_TAG=//p' "releases/$sha/release.env")
    [[ $DASHBOARD_IMAGE_TAG =~ ^sha-[0-9a-f]{40}$ ]] || { log "release $sha has no valid image tag"; exit 1; }
    export DASHBOARD_IMAGE_TAG
}

caddy_started_at() {
    local id
    id=$(compose ps -q caddy 2>/dev/null || true)
    if [[ -n $id ]]; then
        docker inspect -f '{{.State.StartedAt}}' "$id" 2>/dev/null || true
    fi
}

healthy() {
    local body
    for _ in $(seq 1 30); do
        body=$(curl -fsS --max-time 3 "$health_url" 2>/dev/null || true)
        [[ $body == Healthy ]] && return 0
        sleep 2
    done
    return 1
}

# Start the active release's containers and wait until the API is healthy.
start() {
    [[ -f .env ]] || { log "missing $root/.env"; exit 1; }
    compose config --quiet
    local caddy_before
    caddy_before=$(caddy_started_at)
    log "starting services (image $DASHBOARD_IMAGE_TAG)"
    compose up -d --remove-orphans api caddy
    healthy || { log "the API is not healthy"; return 1; }
    # A Caddy container started by this deploy already runs the new config.
    if [[ -n $caddy_before && $caddy_before == "$(caddy_started_at)" ]]; then
        log "reloading caddy"
        compose exec -T caddy caddy reload --config /etc/caddy/Caddyfile >/dev/null
    fi
}

record() {
    local sha=$1
    if [[ -f CURRENT ]] && [[ $(cut -d' ' -f1 CURRENT) != "$sha" ]]; then
        cp CURRENT PREVIOUS
    fi
    printf '%s %s\n' "$sha" "$DASHBOARD_IMAGE_TAG" > CURRENT
    # Keep the newest releases plus whatever CURRENT and PREVIOUS point at.
    local protected
    protected=$(for f in CURRENT PREVIOUS; do
        if [[ -f $f ]]; then cut -d' ' -f1 "$f"; fi
    done | sort -u)
    # Release names are git SHAs, so ls is safe here.
    # shellcheck disable=SC2012
    ls -1t releases | tail -n +$((keep_releases + 1)) | while read -r old; do
        grep -qx "$old" <<<"$protected" || rm -rf "releases/$old"
    done
}

# Back to the release in CURRENT (the one running before this deploy).
restore_current() {
    [[ -f CURRENT ]] || { log "no earlier release to go back to"; return 1; }
    local sha
    sha=$(cut -d' ' -f1 CURRENT)
    log "going back to $sha"
    load_release "$sha"
    install_bundle "releases/$sha"
    start
}

case ${1:-} in
activate)
    bundle=$2 sha=$3
    [[ $sha =~ ^[0-9a-f]{40}$ ]] || { log "invalid git sha: $sha"; exit 1; }
    [[ -f $bundle/release.env ]] || { log "bundle has no release.env"; exit 1; }
    install -d -m 0750 releases
    rm -rf "releases/$sha"
    cp -r "$bundle" "releases/$sha"
    load_release "$sha"
    install_bundle "releases/$sha"
    if [[ ${SKIP_PULL:-} != 1 ]]; then
        log "pulling images"
        compose --profile migrate pull --quiet api migrate
    fi
    # Migrations are additive (expand first, contract in a later release), so
    # the previous image keeps working if the swap below has to be undone.
    log "migrating the database"
    compose --profile migrate run --rm migrate
    if ! start; then
        log "release $sha failed its health check"
        restore_current || log "rollback failed: run deploy.sh status"
        exit 1
    fi
    record "$sha"
    log "active: $sha ($DASHBOARD_IMAGE_TAG)"
    ;;
rollback)
    [[ -f PREVIOUS ]] || { log "no previous release recorded"; exit 1; }
    read -r sha _ < PREVIOUS
    [[ -d releases/$sha ]] || { log "release $sha no longer on disk"; exit 1; }
    log "rolling back to $sha (database migrations stay applied)"
    load_release "$sha"
    install_bundle "releases/$sha"
    start
    record "$sha"
    log "active: $sha ($DASHBOARD_IMAGE_TAG)"
    ;;
compose)
    if [[ -f CURRENT ]]; then
        load_release "$(cut -d' ' -f1 CURRENT)"
    fi
    shift
    compose "$@"
    ;;
status)
    echo "current:  $(cat CURRENT 2>/dev/null || echo none)"
    echo "previous: $(cat PREVIOUS 2>/dev/null || echo none)"
    if [[ -f CURRENT ]]; then
        load_release "$(cut -d' ' -f1 CURRENT)"
        compose ps
    fi
    ;;
*)
    sed -n '2,8p' "$0"
    exit 2
    ;;
esac
