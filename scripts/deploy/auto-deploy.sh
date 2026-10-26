#!/bin/bash
# Install as root:root 0755 /usr/local/sbin/helpdesk-auto-deploy.
set -Eeuo pipefail
if [[ $# != 3 || ! $1 =~ ^[0-9a-f]{40}$ || ! $2 =~ ^sha256:[0-9a-f]{64}$ || ! $3 =~ ^sha256:[0-9a-f]{64}$ ]]; then
    echo 'Usage: helpdesk-auto-deploy COMMIT_SHA API_DIGEST WEB_DIGEST' >&2
    exit 2
fi
[[ $(id -u) == 0 && $(hostname) == homelab-server ]] || exit 1
export PATH=/usr/sbin:/usr/bin:/sbin:/bin HOME=/root DOCKER_CONFIG=/root/.docker
unset DOCKER_HOST DOCKER_CONTEXT DOCKER_TLS_VERIFY DOCKER_CERT_PATH COMPOSE_FILE COMPOSE_PROFILES COMPOSE_PROJECT_NAME COMPOSE_ENV_FILES
umask 077
APP=/opt/helpdesk-auto
COMMIT=$1
export API_IMAGE="ghcr.io/shunbin2310/helpdesk-api@$2"
export WEB_IMAGE="ghcr.io/shunbin2310/helpdesk-web@$3"

for directory in / /opt "$APP"; do
    [[ -d $directory && ! -L $directory && $(stat -c %u "$directory") == 0 ]]
    mode=$(stat -c %a "$directory")
    (( (8#$mode & 8#022) == 0 ))
done
protected_file() {
    [[ -f $1 && ! -L $1 && $(stat -c '%u:%h' "$1") == 0:1 ]]
    local mode
    mode=$(stat -c %a "$1")
    (( (8#$mode & 8#022) == 0 ))
}
protected_file "$APP/compose.yml"
protected_file "$APP/.env"
[[ $(stat -c %a "$APP/.env") == 600 ]]
for file in deploy.lock release.env previous-release.env; do
    if [[ -e $APP/$file || -L $APP/$file ]]; then protected_file "$APP/$file"; fi
done
exec 9<>"$APP/deploy.lock"
flock -n 9 || { echo 'Another deployment is running.' >&2; exit 1; }
[[ ! -e $APP/needs-attention && ! -L $APP/needs-attention ]] || {
    echo 'Previous deployment needs administrator review; do not blindly retry.' >&2
    exit 1
}
compose() {
    docker compose --project-name helpdesk-auto --project-directory "$APP" \
        --env-file "$APP/.env" -f "$APP/compose.yml" "$@"
}
compose config --quiet
# Pull failures must leave the running application alone.
compose pull
printf 'COMMIT_SHA=%s\nAPI_IMAGE=%s\nWEB_IMAGE=%s\n' \
    "$COMMIT" "$API_IMAGE" "$WEB_IMAGE" > "$APP/needs-attention"
started=false
failed() {
    trap - ERR INT TERM
    if [[ $started == true ]]; then compose stop api >/dev/null 2>&1 || true; fi
    echo 'Deployment failed. Keep needs-attention and inspect; no automatic database rollback.' >&2
    exit 1
}
trap failed ERR INT TERM
compose stop api
started=true
# Recreate Web too: Nginx must resolve the new API container address.
compose up -d --force-recreate --no-build --pull never --wait --wait-timeout 420
endpoint=$(compose port web 8080)
[[ $endpoint =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+:8085$ ]]
curl --noproxy '*' --fail --silent --show-error --max-time 15 --output /dev/null "http://$endpoint/api/health"
docker update --restart unless-stopped "$(compose ps -q api)" >/dev/null
if [[ -e $APP/release.env ]]; then
    install -m 0600 "$APP/release.env" "$APP/previous-release.env"
fi
temporary=$(mktemp "$APP/.release.XXXXXXXX")
printf 'COMMIT_SHA=%s\nAPI_IMAGE=%s\nWEB_IMAGE=%s\n' \
    "$COMMIT" "$API_IMAGE" "$WEB_IMAGE" > "$temporary"
mv -T -- "$temporary" "$APP/release.env"
rm -- "$APP/needs-attention"
trap - ERR INT TERM
printf 'Deployed %s. Both containers and the API proxy passed health checks.\n' "$COMMIT"
