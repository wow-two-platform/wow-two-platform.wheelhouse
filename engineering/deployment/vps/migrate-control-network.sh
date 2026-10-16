#!/usr/bin/env bash
# One-time move of an existing host onto the split networks (run as the host administrator from this folder):
#   sudo bash migrate-control-network.sh
# It creates `control`, installs the console allowlist and the new platform Compose file, points both console
# targets at `control`, recreates PostgreSQL and Traefik, then moves the running consoles without restarting them.
# Each step is safe to repeat. Remove this script once every host has moved.
set -euo pipefail

[[ $EUID -eq 0 ]] || { echo 'Usage: sudo bash migrate-control-network.sh' >&2; exit 2; }
script_dir=$(cd "$(dirname "$0")" && pwd)
deploy_root=/srv/wheelhouse
deploy_user=wheelhouse-deploy
deploy_group=$(id -gn "$deploy_user")

if ! docker network inspect control >/dev/null 2>&1; then
    docker network create --driver bridge --subnet 172.30.1.0/24 --gateway 172.30.1.1 control >/dev/null
fi
docker network inspect control | python3 -c '
import json, sys
network = json.load(sys.stdin)[0]
if network.get("IPAM", {}).get("Config") != [{"Subnet": "172.30.1.0/24", "Gateway": "172.30.1.1"}]:
    sys.exit("Existing control network differs; review addresses before proceeding.")
'

# The allowlist must exist before Traefik restarts with it attached, or the console routes are refused.
install -m 0600 -o "$deploy_user" -g "$deploy_group" "$script_dir/control-only.yml" "$deploy_root/ingress/control-only.yml"
install -m 0600 -o "$deploy_user" -g "$deploy_group" "$script_dir/platform.compose.yml" "$deploy_root/platform/compose.yml"

# Point each console target and its settings at the control network, keeping file owners and modes.
python3 - "$deploy_root/config" <<'PY'
import json, os, sys
from pathlib import Path

config = Path(sys.argv[1])

def rewrite(path, change):
    if not path.exists():
        return
    data = json.loads(path.read_text())
    if not change(data):
        return
    stat = path.stat()
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(data, indent=2) + "\n")
    os.chown(temporary, stat.st_uid, stat.st_gid)
    os.chmod(temporary, stat.st_mode & 0o777)
    os.replace(temporary, path)
    print("updated", path.name)

def target(data):
    variables = data.setdefault("variables", {})
    if variables.get("PLATFORM_NETWORK") == "control":
        return False
    variables["PLATFORM_NETWORK"] = "control"
    return True

def settings(data):
    deployment = data.setdefault("Deployment", {})
    if deployment.get("TrustedProxies") == ["172.30.1.2"]:
        return False
    deployment["TrustedProxies"] = ["172.30.1.2"]
    return True

for environment in ("dev", "prod"):
    rewrite(config / ("wheelhouse-" + environment + ".json"), target)
    rewrite(config / ("console-" + environment + ".json"), settings)
PY

# PostgreSQL and Traefik join `control` with fixed addresses; this restarts both for a few seconds.
sudo -u "$deploy_user" docker compose --project-directory "$deploy_root/platform" \
    -f "$deploy_root/platform/compose.yml" --env-file "$deploy_root/platform/host.env" up -d postgres ingress

# Move each running console onto `control` with the aliases Traefik routes to, then off `platform`.
for environment in prod dev; do
    container=wheelhouse-$environment-console-1
    docker inspect "$container" >/dev/null 2>&1 || continue
    networks=$(docker inspect "$container" --format '{{range $name, $_ := .NetworkSettings.Networks}}{{$name}} {{end}}')
    if [[ " $networks " != *" control "* ]]; then
        docker network connect --alias console --alias "wheelhouse-$environment-console" control "$container"
    fi
    if [[ " $networks " == *" platform "* ]]; then
        docker network disconnect platform "$container"
    fi
    echo "$container is on: $(docker inspect "$container" --format '{{range $name, $_ := .NetworkSettings.Networks}}{{$name}} {{end}}')"
done

# A workload on `platform` (a canary node stands in) must reach neither a console nor its entry point.
host=$(python3 -c 'import json, sys; print(json.load(open(sys.argv[1]))["ingress"]["hosts"]["console"])' \
    "$deploy_root/config/wheelhouse-prod.json")
probe=wheelhouse-platform-canary-dev-1
echo "Through Traefik: $(docker exec "$probe" wget -q -S -O /dev/null --header "Host: $host" \
    http://172.30.0.2:8080/ 2>&1 | awk '/HTTP\//{print $2; exit}') (expected 403)"
# Any HTTP status line, even a 400 from host filtering, means the container is still reachable.
if docker exec "$probe" wget -q -T 3 -S -O /dev/null --header "Host: $host" \
    http://wheelhouse-prod-console:8080/health 2>&1 | grep -q 'HTTP/'; then
    echo 'Direct: the prod console still answers a workload (expected no route).' >&2; exit 1
fi
echo 'Direct: the prod console is unreachable from a workload.'
