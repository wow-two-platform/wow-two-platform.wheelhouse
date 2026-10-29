#!/usr/bin/env bash
# One-time removal of the retired `test` environment from an existing host (run as the host administrator from this
# folder, after the consoles run a release with migration 013):
#   sudo bash retire-test.sh
# It installs the platform Compose file without the canary-test node, removes that node and its volume, takes down the
# canary's test stack with its route, state, settings and database, then lists any other test stack or database left.
# Each step is safe to repeat. Remove this script once every host is clean.
set -euo pipefail

[[ $EUID -eq 0 ]] || { echo 'Usage: sudo bash retire-test.sh' >&2; exit 2; }
script_dir=$(cd "$(dirname "$0")" && pwd)
deploy_root=/srv/wheelhouse
deploy_user=wheelhouse-deploy
deploy_group=$(id -gn "$deploy_user")
compose=(sudo -u "$deploy_user" docker compose --project-directory "$deploy_root/platform"
    -f "$deploy_root/platform/compose.yml" --env-file "$deploy_root/platform/host.env")

# PostgreSQL 18 never starts empty beside a host's 16 data: postgres-move.py moves it first (vps.md § PostgreSQL 18).
python3 "$script_dir/postgres-move.py" check --root "$deploy_root" --project wheelhouse-platform || exit 1
install -m 0600 -o "$deploy_user" -g "$deploy_group" "$script_dir/platform.compose.yml" "$deploy_root/platform/compose.yml"
"${compose[@]}" up -d --remove-orphans
docker volume rm wheelhouse-platform_canary-test 2>/dev/null || true

sudo -u "$deploy_user" docker compose -p wheelhousecanary-test down --remove-orphans 2>/dev/null || true
rm -f "$deploy_root/ingress/wheelhousecanary-test.yml" "$deploy_root/config/canary-test.json"
rm -rf "$deploy_root/wheelhousecanary-test"
"${compose[@]}" exec -T postgres psql -U postgres -v ON_ERROR_STOP=1 \
    -c 'DROP DATABASE IF EXISTS wheelhousecanary_test' -c 'DROP ROLE IF EXISTS wheelhousecanary_test' >/dev/null

echo "Test stacks still running (expected none):"
docker ps -a --format '{{.Label "com.docker.compose.project"}}' | sort -u | grep -- '-test$' | sed 's/^/  /' || echo "  none"
echo "Test databases left (expected none):"
"${compose[@]}" exec -T postgres psql -U postgres -At \
    -c "SELECT datname FROM pg_database WHERE datname LIKE '%\_test'" | sed 's/^/  /' | grep . || echo "  none"
echo "Now remove the canary-test machine in the Tailscale admin console (Machines › canary-test › Remove)."
