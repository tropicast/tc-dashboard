#!/usr/bin/env bash
# Sign-up to live broadcast (#17): builds the e2e stack (compose.yaml) with
# fresh secrets, runs web/e2e/live-broadcast.spec.ts against it, then removes it.
# Needs Docker with Compose, Node, ffmpeg (libmp3lame, libopus) and a
# tc-streaming checkout (TC_STREAMING_DIR, default ../tc-streaming).
# E2E_KEEP=1 leaves the stack running for debugging.
set -euo pipefail

root=$(cd "$(dirname "$0")/../.." && pwd)
streaming=$(cd "${TC_STREAMING_DIR:-$root/../tc-streaming}" && pwd)
compose_file=$root/tests/e2e/compose.yaml
env_file=$(mktemp)
chmod 600 "$env_file"

cat >"$env_file" <<ENV
TC_STREAMING_DIR=$streaming
E2E_DB_PASSWORD=$(openssl rand -hex 16)
E2E_SOURCE_AUTH_PASSWORD=$(openssl rand -hex 24)
E2E_ICECAST_SOURCE_PASSWORD=$(openssl rand -hex 24)
E2E_ICECAST_ADMIN_PASSWORD=$(openssl rand -hex 24)
E2E_API_PORT=${E2E_API_PORT:-18080}
E2E_ICECAST_PORT=${E2E_ICECAST_PORT:-18000}
E2E_MAIL_PORT=${E2E_MAIL_PORT:-18025}
ENV
# shellcheck disable=SC1090
source "$env_file"

compose() { docker compose -f "$compose_file" --env-file "$env_file" "$@"; }
cleanup() {
    status=$?
    if [[ $status -ne 0 ]]; then
        compose ps -a || true
        compose logs --no-color --tail 200 || true
    fi
    if [[ -z ${E2E_KEEP:-} ]]; then
        compose down -v --remove-orphans >/dev/null 2>&1 || true
        rm -f "$env_file"
    else
        echo "Stack kept; env file: $env_file"
    fi
    exit "$status"
}
trap cleanup EXIT

compose up -d --build --wait --wait-timeout 300

cd "$root/web"
REAL_E2E=1 REAL_E2E_BROADCAST=1 \
    REAL_E2E_BASE_URL=http://localhost:$E2E_API_PORT \
    REAL_E2E_MAILPIT_URL=http://localhost:$E2E_MAIL_PORT \
    E2E_COMPOSE_FILE=$compose_file E2E_ENV_FILE=$env_file \
    npx playwright test e2e/live-broadcast.spec.ts "$@"
