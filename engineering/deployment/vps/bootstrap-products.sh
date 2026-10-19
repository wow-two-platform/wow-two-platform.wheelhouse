#!/usr/bin/env bash
# Run on the verified fresh Ubuntu VPS that hosts products only, never from CI. Review vps.md § Products host first.
# Unlike bootstrap.sh (the control host), this role also sets SSH, firewall, tailnet and security-update policy.
# Every step converges on the same state, so the script is safe to re-run. Sourcing it defines the steps without
# running them; the local rehearsal lays out its products host with the same functions.
set -euo pipefail
umask 077

deploy_user=wheelhouse-deploy
deploy_home=/home/wheelhouse-deploy
deploy_root=/srv/wheelhouse
# The same `platform` addresses as the control host; Docker hands out workload addresses only from .128 up, so the
# fixed ones (Traefik .2, PostgreSQL .3, site nodes 172.30.0.64/26) can never be taken by a workload.
platform_subnet=172.30.0.0/24
platform_range=172.30.0.128/25
platform_gateway=172.30.0.1
tailnet_tag=tag:products
ssh_policy=/etc/ssh/sshd_config.d/01-wheelhouse-products.conf
kit_assets=(products.compose.yml tailscale-serve-products.json prepare.py authorize-runner.sh)

# Writes stdin to a file through a temporary copy beside it: Ubuntu 26.04's uutils install cannot replace an
# existing file from /dev/stdin ("No such file or directory").
write_file() {  # mode owner group destination < content
    local mode=$1 owner=$2 group=$3 destination=$4 temporary
    temporary=$(mktemp "$(dirname "$destination")/.write.XXXXXX")
    if ! cat > "$temporary" || ! install -m "$mode" -o "$owner" -g "$group" "$temporary" "$destination"; then
        rm -f "$temporary"
        return 1
    fi
    rm -f "$temporary"
}

usage() {
    cat <<'EOF'
Usage: sudo bash bootstrap-products.sh --host-identity-verified [--tailnet-hostname <name>]
                                       [--tailnet-only-ssh | --public-ssh]
Prepares a products host: Docker, the wheelhouse-deploy account, the products kit and `platform` network, SSH keys
only, the firewall, unattended security updates and the tailnet (tag:products).
  --tailnet-hostname <name>  Join the tailnet as <name> (prints a sign-in link) unless it already has.
  --tailnet-only-ssh         Close public SSH; needs an open SSH session over the tailnet.
  --public-ssh               Reopen public SSH: the fallback when the tailnet path is lost.
EOF
}

parse_arguments() {
    verified=false tailnet_hostname='' ssh_mode=keep
    while (($#)); do
        case "$1" in
            --host-identity-verified) verified=true; shift ;;
            --tailnet-hostname) tailnet_hostname=${2:?Missing tailnet hostname}; shift 2 ;;
            --tailnet-only-ssh) [[ $ssh_mode == keep ]] || { usage >&2; exit 2; }; ssh_mode=tailnet; shift ;;
            --public-ssh) [[ $ssh_mode == keep ]] || { usage >&2; exit 2; }; ssh_mode=public; shift ;;
            --help|-h) usage; exit 0 ;;
            *) usage >&2; exit 2 ;;
        esac
    done
    [[ $EUID -eq 0 && $verified == true ]] || { usage >&2; exit 2; }
    [[ -z $tailnet_hostname || $tailnet_hostname =~ ^[a-z][a-z0-9-]{0,61}[a-z0-9]$ ]] || {
        echo 'Use a lowercase tailnet hostname such as ocharo-vps.' >&2; exit 2;
    }
}

require_host() {
    . /etc/os-release
    [[ $ID == ubuntu && $VERSION_ID == 26.04 && $(dpkg --print-architecture) == amd64 ]] || {
        echo 'This bootstrap is reviewed only for Ubuntu 26.04 amd64.' >&2; exit 1;
    }
    # Password sign-in is turned off below; the administrator running this must already sign in with a key.
    [[ -n ${SUDO_USER:-} && $SUDO_USER != root ]] || {
        echo 'Run through sudo from the administrator account, so its SSH key can be checked first.' >&2
        echo 'A root-only image (Contabo) has none yet: create it with create-admin.sh, as vps.md describes.' >&2
        exit 1
    }
    local admin_keys
    admin_keys=$(getent passwd "$SUDO_USER" | cut -d: -f6)/.ssh/authorized_keys
    [[ -f $admin_keys ]] && awk 'NF && $1 !~ /^#/ {found = 1} END {exit !found}' "$admin_keys" || {
        echo "Add your SSH public key to $admin_keys and sign in with it first: password sign-in is turned off." >&2
        exit 1
    }
    for asset in "${kit_assets[@]}"; do
        [[ -f "$script_dir/$asset" ]] || { echo "Missing bootstrap asset: $asset" >&2; exit 1; }
    done
    # Fail rather than removing a pre-existing container stack's engine packages.
    for package in docker.io docker-compose docker-compose-v2 podman-docker containerd runc; do
        if dpkg-query -W -f='${Status}' "$package" 2>/dev/null | grep -qx 'install ok installed'; then
            echo "Review conflicting installed package manually: $package" >&2; exit 1
        fi
    done
}

# Docker and Tailscale from their official repositories; an installed package is never upgraded here, because a
# Docker upgrade restarts every container on the host.
install_packages() {
    export DEBIAN_FRONTEND=noninteractive
    apt-get update
    apt-get install -y --no-upgrade ca-certificates curl openssh-server python3 ufw unattended-upgrades
    install -d -m 0755 /etc/apt/keyrings /usr/share/keyrings
    curl --fail --silent --show-error --location --proto '=https' \
        https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
    write_file 0644 root root /etc/apt/sources.list.d/docker.sources <<EOF
Types: deb
URIs: https://download.docker.com/linux/ubuntu
Suites: $VERSION_CODENAME
Components: stable
Architectures: amd64
Signed-By: /etc/apt/keyrings/docker.asc
EOF
    curl --fail --silent --show-error --location --proto '=https' \
        https://pkgs.tailscale.com/stable/ubuntu/resolute.noarmor.gpg \
        -o /usr/share/keyrings/tailscale-archive-keyring.gpg
    curl --fail --silent --show-error --location --proto '=https' \
        https://pkgs.tailscale.com/stable/ubuntu/resolute.tailscale-keyring.list \
        -o /etc/apt/sources.list.d/tailscale.list
    chmod 0644 /etc/apt/keyrings/docker.asc /usr/share/keyrings/tailscale-archive-keyring.gpg \
        /etc/apt/sources.list.d/tailscale.list
    apt-get update
    apt-get install -y --no-upgrade docker-ce docker-ce-cli containerd.io docker-buildx-plugin \
        docker-compose-plugin tailscale
    systemctl enable --now docker ssh tailscaled
}

# The deploy account Wheelhouse prod's runner signs in as. It joins the Docker group, which grants host control; its
# only keys are runner keys that authorize-runner.sh --accept adds with `restrict`.
ensure_deploy_account() {
    if ! id "$deploy_user" >/dev/null 2>&1; then
        useradd --create-home --shell /bin/bash "$deploy_user"
    fi
    [[ $(id -u "$deploy_user") != 0 && $(getent passwd "$deploy_user" | cut -d: -f6) == "$deploy_home" ]] || {
        echo 'Existing deployment account does not match the dedicated account contract.' >&2; exit 1;
    }
    usermod --append --groups docker "$deploy_user"
    deploy_group=$(id -gn "$deploy_user")
    install -d -m 0700 -o "$deploy_user" -g "$deploy_group" "$deploy_home/.ssh"
    local authorized_keys=$deploy_home/.ssh/authorized_keys
    [[ ! -L $authorized_keys ]] || { echo 'Refusing a symlinked authorized_keys.' >&2; exit 1; }
    touch "$authorized_keys"
    chown "$deploy_user:$deploy_group" "$authorized_keys"
    chmod 0600 "$authorized_keys"
    if awk 'NF && $1 !~ /^#/ && $1 != "restrict" {found = 1} END {exit !found}' "$authorized_keys"; then
        echo "Review $authorized_keys: a key there lacks restrict." >&2
    fi
}

# The deployment root and the products kit: Compose template, site Serve config, the configuration generator, and the
# runner-key script that only the administrator may change.
lay_out_kit() {
    local kit=$1 directory
    deploy_group=$(id -gn "$deploy_user")
    for directory in "$deploy_root" "$deploy_root/config" "$deploy_root/platform" "$deploy_root/platform/.secrets" \
        "$deploy_root/platform/sites" "$deploy_root/ingress" "$deploy_root/bootstrap" "$deploy_root/private"; do
        [[ ! -L $directory ]] || { echo 'Refusing a symlinked deployment directory.' >&2; exit 1; }
        install -d -m 0700 -o "$deploy_user" -g "$deploy_group" "$directory"
    done
    install -m 0600 -o "$deploy_user" -g "$deploy_group" \
        "$kit/products.compose.yml" "$deploy_root/platform/products.compose.yml"
    install -m 0644 -o "$deploy_user" -g "$deploy_group" \
        "$kit/tailscale-serve-products.json" "$deploy_root/platform/tailscale-serve.json"
    install -m 0500 -o "$deploy_user" -g "$deploy_group" "$kit/prepare.py" "$deploy_root/bootstrap/prepare.py"
    install -m 0500 -o root -g root "$kit/authorize-runner.sh" "$deploy_root/bootstrap/authorize-runner.sh"
    # Traefik runs as the deploy account so it reads the runner's mode-0600 route files without extra capabilities.
    printf 'WHEELHOUSE_DEPLOY_UID=%s\nWHEELHOUSE_DEPLOY_GID=%s\n' \
        "$(id -u "$deploy_user")" "$(id -g "$deploy_user")" > "$deploy_root/platform/host.env"
    chown "$deploy_user:$deploy_group" "$deploy_root/platform/host.env"
    chmod 0600 "$deploy_root/platform/host.env"
}

# Products and their site nodes share one bridge with PostgreSQL and Traefik; an incompatible existing one is refused.
ensure_platform_network() {
    if ! docker network inspect platform >/dev/null 2>&1; then
        docker network create --driver bridge --subnet "$platform_subnet" --ip-range "$platform_range" \
            --gateway "$platform_gateway" platform >/dev/null
    fi
    docker network inspect platform | python3 -c '
import json, sys
network = json.load(sys.stdin)[0]
expected = [{"Subnet": sys.argv[1], "IPRange": sys.argv[2], "Gateway": sys.argv[3]}]
if network.get("Driver") != "bridge" or network.get("IPAM", {}).get("Config") != expected:
    sys.exit("Existing platform network differs; review addresses before proceeding.")
' "$platform_subnet" "$platform_range" "$platform_gateway"
}

# Keys only: no passwords, no keyboard-interactive and no root sign-in. The drop-in sorts first, and sshd keeps the
# first value it reads, so a later cloud-init file cannot turn passwords back on.
write_ssh_policy() {
    install -d -m 0755 "$(dirname "$ssh_policy")"
    local previous=
    [[ -f $ssh_policy ]] && previous=$(cat "$ssh_policy")
    write_file 0644 root root "$ssh_policy" <<'EOF'
# Written by bootstrap-products.sh: SSH keys only.
PubkeyAuthentication yes
PasswordAuthentication no
KbdInteractiveAuthentication no
PermitEmptyPasswords no
PermitRootLogin no
X11Forwarding no
EOF
    # A socket-activated sshd may not have created its privilege-separation folder yet; `sshd -t` needs it.
    install -d -m 0755 /run/sshd
    if ! sshd -t; then
        if [[ -n $previous ]]; then printf '%s\n' "$previous" > "$ssh_policy"; else rm -f "$ssh_policy"; fi
        echo 'sshd refused the SSH policy; the previous configuration stays.' >&2; exit 1
    fi
}

harden_ssh() {
    write_ssh_policy
    systemctl try-reload-or-restart ssh
    local effective
    effective=$(sshd -T)
    for setting in 'passwordauthentication no' 'kbdinteractiveauthentication no' 'permitrootlogin no'; do
        grep -qx "$setting" <<<"$effective" || { echo "sshd still runs without '$setting'; review its drop-ins." >&2; exit 1; }
    done
}

# Security updates install unattended; reboots stay with the owner, and Docker and Tailscale never upgrade unattended.
enable_security_upgrades() {
    write_file 0644 root root /etc/apt/apt.conf.d/20auto-upgrades <<'EOF'
APT::Periodic::Update-Package-Lists "1";
APT::Periodic::Unattended-Upgrade "1";
EOF
    write_file 0644 root root /etc/apt/apt.conf.d/52wheelhouse-unattended-upgrades <<'EOF'
// Written by bootstrap-products.sh. An engine upgrade restarts every container, so the owner upgrades Docker.
Unattended-Upgrade::Automatic-Reboot "false";
Unattended-Upgrade::Package-Blacklist {
    "docker-ce"; "docker-ce-cli"; "containerd.io"; "docker-buildx-plugin"; "docker-compose-plugin"; "tailscale";
};
EOF
    systemctl enable --now unattended-upgrades apt-daily.timer apt-daily-upgrade.timer
}

tailnet_state() {
    tailscale status --json 2>/dev/null | python3 -c '
import json, sys
status = json.load(sys.stdin)
print(status.get("BackendState", ""), " ".join((status.get("Self") or {}).get("Tags") or []))
' 2>/dev/null || echo 'Stopped'
}

# The host joins as tag:products: Wheelhouse prod's control node reaches its SSH and nothing else (tailnet-policy.hujson).
# A node tagged at sign-in keeps its key without expiry. The policy must define the tag first.
join_tailnet() {
    local state tags
    read -r state tags <<<"$(tailnet_state)"
    if [[ $state != Running ]]; then
        if [[ -z $tailnet_hostname ]]; then
            echo 'Tailscale is installed but not joined; rerun with --tailnet-hostname <name>.' >&2
            return
        fi
        tailscale up --hostname="$tailnet_hostname" --advertise-tags="$tailnet_tag" \
            --accept-dns=false --accept-routes=false --ssh=false
        read -r state tags <<<"$(tailnet_state)"
    fi
    [[ " $tags " == *" $tailnet_tag "* ]] || echo "This node lacks $tailnet_tag; tag it in the admin console." >&2
}

# Peer addresses of established SSH sessions on stdin (`ss -Htn` lines); succeeds when one comes over the tailnet.
tailnet_session() {
    python3 -c '
import ipaddress, sys
tailnet = (ipaddress.ip_network("100.64.0.0/10"), ipaddress.ip_network("fd7a:115c:a1e0::/48"))
for line in sys.stdin:
    fields = line.split()
    if len(fields) >= 4:
        host = fields[3].rsplit(":", 1)[0].strip("[]").split("%")[0]
        try:
            address = ipaddress.ip_address(host)
        except ValueError:
            continue
        address = getattr(address, "ipv4_mapped", None) or address
        if any(address in network for network in tailnet if network.version == address.version):
            sys.exit(0)
sys.exit(1)
'
}

# Default deny inbound. SSH stays public until --tailnet-only-ssh closes it; 80 and 443 stay closed until public ingress
# ships. Docker publishes only on loopback here, so its rules open nothing. A rerun keeps the current SSH exposure.
configure_firewall() {
    local active=false
    ufw status | grep -qx 'Status: active' && active=true
    if [[ $ssh_mode == tailnet ]]; then
        read -r state _ <<<"$(tailnet_state)"
        [[ $state == Running ]] && ip link show tailscale0 >/dev/null 2>&1 || {
            echo 'Tailscale is not running; public SSH stays open.' >&2; exit 1;
        }
        ss -Htn state established '( sport = :22 )' | tailnet_session || {
            echo 'Open an SSH session over the tailnet first and keep it open; public SSH stays open.' >&2; exit 1;
        }
    fi
    ufw default deny incoming >/dev/null
    ufw default allow outgoing >/dev/null
    ufw allow in on tailscale0 to any port 22 proto tcp comment 'ssh over the tailnet' >/dev/null
    # WireGuard's direct path; without it the tailnet still works through relays, only slower.
    ufw allow 41641/udp comment 'tailscale direct' >/dev/null
    if [[ $ssh_mode == public || ( $ssh_mode == keep && $active == false ) ]]; then
        ufw allow 22/tcp comment 'public ssh' >/dev/null
    elif [[ $ssh_mode == tailnet ]]; then
        ufw --force delete allow 22/tcp >/dev/null 2>&1 || true
    fi
    ufw --force enable >/dev/null
    ufw status verbose
}

main() {
    parse_arguments "$@"
    script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
    require_host
    install_packages
    ensure_deploy_account
    lay_out_kit "$script_dir"
    ensure_platform_network
    harden_ssh
    enable_security_upgrades
    join_tailnet
    configure_firewall
    docker compose version
    echo 'Products host prepared. Continue with vps.md § Products host: tailnet, prepare.py, runner key.'
}

if [[ ${BASH_SOURCE[0]} == "$0" ]]; then
    main "$@"
fi
