#!/usr/bin/env bash
# Run on the verified fresh Ubuntu VPS, never from CI. Review vps.md first.
set -euo pipefail
umask 077

usage() {
    cat <<'EOF'
Usage: sudo bash bootstrap.sh --host-identity-verified --ssh-public-key /path/to/ci.pub [--install-tailscale]
Installs host dependencies and a Docker-privileged wheelhouse-deploy account.
Leaves SSH/firewall policy, Tailscale enrollment and application startup to the operator.
EOF
}

verified=false
public_key=
install_tailscale=false
while (($#)); do
    case "$1" in
        --host-identity-verified) verified=true; shift ;;
        --ssh-public-key) public_key=${2:?Missing public key path}; shift 2 ;;
        --install-tailscale) install_tailscale=true; shift ;;
        --help|-h) usage; exit 0 ;;
        *) usage >&2; exit 2 ;;
    esac
done
[[ $EUID -eq 0 && $verified == true && -f "$public_key" && ! -L "$public_key" ]] || {
    usage >&2; exit 2;
}
. /etc/os-release
[[ $ID == ubuntu && $VERSION_ID == 26.04 && $(dpkg --print-architecture) == amd64 ]] || {
    echo 'This bootstrap is reviewed only for Ubuntu 26.04 amd64.' >&2; exit 1;
}
ssh-keygen -l -f "$public_key" >/dev/null
[[ $(awk 'NF && $1 !~ /^#/ {count++} END {print count+0}' "$public_key") == 1 ]] || {
    echo 'Provide exactly one SSH public key.' >&2; exit 1;
}
[[ $(awk 'NF {print $1; exit}' "$public_key") == ssh-ed25519 ]] || {
    echo 'Provide an Ed25519 public key without authorized_keys options.' >&2; exit 1;
}

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
deploy_user=wheelhouse-deploy
deploy_root=/srv/wheelhouse
for source in platform.compose.yml tailscale-serve.json control-only.yml prepare.py prepare_canary.py authorize-runner.sh; do
    [[ -f "$script_dir/$source" ]] || { echo "Missing bootstrap asset: $source" >&2; exit 1; }
done
# Fail rather than removing a pre-existing container stack's engine packages.
for package in docker.io docker-compose docker-compose-v2 podman-docker containerd runc; do
    if dpkg-query -W -f='${Status}' "$package" 2>/dev/null | grep -qx 'install ok installed'; then
        echo "Review conflicting installed package manually: $package" >&2; exit 1
    fi
done

export DEBIAN_FRONTEND=noninteractive
apt-get update
apt-get install -y --no-upgrade ca-certificates curl openssh-server python3 unattended-upgrades
install -d -m 0755 /etc/apt/keyrings
curl --fail --silent --show-error --location --proto '=https' \
    https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
chmod 0644 /etc/apt/keyrings/docker.asc
cat > /etc/apt/sources.list.d/docker.sources <<EOF
Types: deb
URIs: https://download.docker.com/linux/ubuntu
Suites: $VERSION_CODENAME
Components: stable
Architectures: amd64
Signed-By: /etc/apt/keyrings/docker.asc
EOF
chmod 0644 /etc/apt/sources.list.d/docker.sources
if [[ $install_tailscale == true ]]; then
    install -d -m 0755 /usr/share/keyrings
    curl --fail --silent --show-error --location --proto '=https' \
        https://pkgs.tailscale.com/stable/ubuntu/resolute.noarmor.gpg \
        -o /usr/share/keyrings/tailscale-archive-keyring.gpg
    curl --fail --silent --show-error --location --proto '=https' \
        https://pkgs.tailscale.com/stable/ubuntu/resolute.tailscale-keyring.list \
        -o /etc/apt/sources.list.d/tailscale.list
    chmod 0644 /usr/share/keyrings/tailscale-archive-keyring.gpg /etc/apt/sources.list.d/tailscale.list
fi
apt-get update
apt-get install -y --no-upgrade docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
systemctl enable --now docker ssh
if [[ $install_tailscale == true ]]; then
    apt-get install -y --no-upgrade tailscale
    systemctl enable --now tailscaled
fi

if ! id "$deploy_user" >/dev/null 2>&1; then
    useradd --create-home --shell /bin/bash "$deploy_user"
fi
[[ $(id -u "$deploy_user") != 0 && $(getent passwd "$deploy_user" | cut -d: -f6) == /home/wheelhouse-deploy ]] || {
    echo 'Existing deployment account does not match the dedicated account contract.' >&2; exit 1;
}
usermod --append --groups docker "$deploy_user"
deploy_group=$(id -gn "$deploy_user")
install -d -m 0700 -o "$deploy_user" -g "$deploy_group" /home/wheelhouse-deploy/.ssh
authorized_keys=/home/wheelhouse-deploy/.ssh/authorized_keys
[[ ! -L "$authorized_keys" ]] || { echo 'Refusing a symlinked authorized_keys.' >&2; exit 1; }
touch "$authorized_keys"
key_line=$(awk 'NF && $1 !~ /^#/ {print $1 " " $2}' "$public_key")
if ! grep -Fq -- "$key_line" "$authorized_keys"; then
    # This CI identity requires SCP/SSH commands, not agent forwarding, tunnels or a terminal.
    printf 'restrict %s\n' "$key_line" >> "$authorized_keys"
fi
chown "$deploy_user:$deploy_group" "$authorized_keys"
chmod 0600 "$authorized_keys"

for directory in "$deploy_root" "$deploy_root/config" "$deploy_root/platform" \
    "$deploy_root/platform/.secrets" "$deploy_root/ingress" "$deploy_root/bootstrap"; do
    [[ ! -L "$directory" ]] || { echo 'Refusing a symlinked deployment directory.' >&2; exit 1; }
    install -d -m 0700 -o "$deploy_user" -g "$deploy_group" "$directory"
done
install -m 0600 -o "$deploy_user" -g "$deploy_group" \
    "$script_dir/platform.compose.yml" "$deploy_root/platform/compose.yml"
install -m 0644 -o "$deploy_user" -g "$deploy_group" \
    "$script_dir/tailscale-serve.json" "$deploy_root/platform/tailscale-serve.json"
# The console entry points require this middleware; Traefik refuses their routes until it exists.
install -m 0600 -o "$deploy_user" -g "$deploy_group" \
    "$script_dir/control-only.yml" "$deploy_root/ingress/control-only.yml"
install -m 0500 -o "$deploy_user" -g "$deploy_group" \
    "$script_dir/prepare.py" "$script_dir/prepare_canary.py" "$deploy_root/bootstrap/"
# The administrator runs this one; the deploy account must not be able to change it.
install -m 0500 -o root -g root "$script_dir/authorize-runner.sh" "$deploy_root/bootstrap/authorize-runner.sh"
# Traefik must read mode-0600 route files written by the deploy account without extra capabilities.
printf 'WHEELHOUSE_DEPLOY_UID=%s\nWHEELHOUSE_DEPLOY_GID=%s\n' \
    "$(id -u "$deploy_user")" "$(id -g "$deploy_user")" > "$deploy_root/platform/host.env"
chown "$deploy_user:$deploy_group" "$deploy_root/platform/host.env"
chmod 0600 "$deploy_root/platform/host.env"

# Fixed addresses make forwarded-header trust explicit. Docker hands out dynamic addresses only from .128/25, so a
# container that starts first can never take Traefik's .2, PostgreSQL's .3 or a canary node's address. An existing
# network without that range is repaired by repair-networks.sh in a maintenance step; any other difference is refused.
# control: consoles, PostgreSQL, Traefik. platform: product and canary workloads, PostgreSQL, Traefik.
ensure_network() {
    local name=$1 subnet=$2 gateway=$3 ip_range=$4
    if ! docker network inspect "$name" >/dev/null 2>&1; then
        docker network create --driver bridge --subnet "$subnet" --ip-range "$ip_range" --gateway "$gateway" \
            "$name" >/dev/null
    fi
    docker network inspect "$name" | python3 -c '
import json, sys
network, subnet, gateway, ip_range = json.load(sys.stdin)[0], *sys.argv[1:4]
config = network.get("IPAM", {}).get("Config")
if network.get("Driver") == "bridge" and config == [{"Subnet": subnet, "Gateway": gateway}]:
    sys.exit("Existing " + network["Name"] + " network lets a dynamic container take a fixed address; run "
             "repair-networks.sh in a maintenance step (vps.md, Network repair).")
if network.get("Driver") != "bridge" or config != [{"Subnet": subnet, "IPRange": ip_range, "Gateway": gateway}]:
    sys.exit("Existing " + network.get("Name", "?") + " network differs; review addresses before proceeding.")
' "$subnet" "$gateway" "$ip_range"
}
ensure_network platform 172.30.0.0/24 172.30.0.1 172.30.0.128/25
ensure_network control 172.30.1.0/24 172.30.1.1 172.30.1.128/25
docker compose version
echo 'Host dependencies prepared. Complete private settings and access checks in vps.md before starting services.'
