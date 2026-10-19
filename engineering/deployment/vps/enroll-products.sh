#!/usr/bin/env bash
# Wheelhouse prod's enrollment entry point on a fresh products host. Its runner uploads this kit over the public SSH
# session its one-time key opens and runs one phase per step, as root (directly, or through `sudo -n`):
#   admin <name>          the administrator the owner signs in as; stdin holds the owner's public keys
#   packages              Docker, Tailscale, OpenSSH, UFW and unattended upgrades (bootstrap-products.sh)
#   host                  the deploy account, the kit, the `platform` network, security updates; public SSH kept
#   join <hostname>       the tailnet as tag:products; stdin holds a single-use auth key Wheelhouse minted
#   accept-runner         authorizes prod's runner key for wheelhouse-deploy; stdin holds that public key
#   seal <admin>          keys-only SSH, no root sign-in, public SSH closed, the one-time key removed; stdin holds it
# Every phase converges, so a rerun is safe. Secrets arrive on stdin only and never reach an argument, a log or a file
# outside /run. Sourcing it defines the phases without running them. By hand, use bootstrap-products.sh (vps.md).
set -euo pipefail
umask 077

enroll_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
# shellcheck source=create-admin.sh
. "$enroll_dir/create-admin.sh"
# shellcheck source=bootstrap-products.sh
. "$enroll_dir/bootstrap-products.sh"
# shellcheck source=repair-networks.sh
. "$enroll_dir/repair-networks.sh"
script_dir=$enroll_dir
ssh_mode=keep tailnet_hostname=''
# Where stdin secrets land for a moment: memory, root-only.
secret_dir=${secret_dir:-/run}

enroll_usage() {
    echo 'Usage: enroll-products.sh admin <name> | packages | host | join <hostname> | accept-runner | seal <admin>' >&2
    exit 2
}

require_enrolling_root() {
    [[ $EUID -eq 0 ]] || { echo 'Run as root, or through sudo -n.' >&2; exit 2; }
}

valid_admin() {
    [[ $1 =~ ^[a-z][a-z0-9-]{0,30}$ && $1 != root && $1 != "$deploy_user" ]] || {
        echo 'Name the administrator something other than root or the deploy account.' >&2; exit 2;
    }
}

# Whether an account holds at least one SSH key line other than the one given (the one-time key).
holds_own_key() {  # <account> [<key material to ignore>]
    local home file
    home=$(getent passwd "$1" | cut -d: -f6) || return 1
    file=$home/.ssh/authorized_keys
    [[ -f $file && ! -L $file ]] || return 1
    [[ -n $(key_lines "$file" | { if [[ -n ${2:-} ]]; then grep -vF -- "$2" || true; else cat; fi; }) ]]
}

# The administrator the owner signs in as once root sign-in is off. An account that already holds keys is kept as it
# is; a new one gets the owner's keys from stdin and no password (the owner sets one for the provider's console).
phase_admin() {
    local keys
    valid_admin "$1"
    admin=$1
    if getent passwd "$admin" >/dev/null && holds_own_key "$admin"; then
        echo "Administrator $admin is kept as it is: it already signs in with its own keys."
        return
    fi
    keys=$(mktemp "$secret_dir/wheelhouse-admin-keys.XXXXXX")
    cat > "$keys"
    admin_keys_source=$keys create_admin || { rm -f "$keys"; return 1; }
    rm -f "$keys"
}

phase_packages() {
    require_platform
    install_packages
}

phase_host() {
    require_platform
    ensure_deploy_account
    lay_out_kit "$script_dir"
    # A `platform` network made before the dynamic range existed is recreated with it; a new one gets it at creation.
    repair_network platform "$platform_subnet" "$platform_gateway" "$platform_range" >/dev/null
    ensure_platform_network
    enable_security_upgrades
    ssh_mode=keep configure_firewall >/dev/null
    echo 'Deploy account, kit, platform network, security updates and firewall in place; public SSH stays open.'
}

# The single-use key lands in a private file under /run (memory), is handed to tailscale as file:, and goes at once.
phase_join() {
    local key_file state tags address host_key
    [[ $1 =~ ^[a-z][a-z0-9-]{0,61}[a-z0-9]$ ]] || { echo 'Use a lowercase tailnet hostname.' >&2; exit 2; }
    tailnet_hostname=$1
    key_file=$(mktemp "$secret_dir/wheelhouse-authkey.XXXXXX")
    cat > "$key_file"
    if ! auth_key_file=$key_file join_tailnet; then
        rm -f "$key_file"
        echo 'tailscale up failed; the key may have expired. Rerun the enrollment for a new one.' >&2
        exit 1
    fi
    rm -f "$key_file"
    read -r state tags <<<"$(tailnet_state)"
    [[ $state == Running ]] || { echo 'The node did not come up on the tailnet.' >&2; exit 1; }
    [[ " $tags " == *" $tailnet_tag "* ]] || { echo "The node lacks $tailnet_tag." >&2; exit 1; }
    address=$(tailscale ip -4 | head -n 1)
    host_key=$(cut -d' ' -f1,2 /etc/ssh/ssh_host_ed25519_key.pub)
    printf '{"address": "%s", "hostKey": "%s"}\n' "$address" "$host_key"
}

phase_accept_runner() {
    local key_file
    key_file=$(mktemp "$secret_dir/wheelhouse-runner.XXXXXX")
    cat > "$key_file"
    bash "$deploy_root/bootstrap/authorize-runner.sh" --accept "$key_file" || { rm -f "$key_file"; return 1; }
    rm -f "$key_file"
}

# Removes the lines holding one key from an account's authorized_keys, keeping its owner and mode.
remove_key() {  # <account> <key material>
    local home file owner group
    getent passwd "$1" >/dev/null || return 0
    home=$(getent passwd "$1" | cut -d: -f6)
    file=$home/.ssh/authorized_keys
    [[ -f $file && ! -L $file ]] || return 0
    grep -qF -- "$2" "$file" || return 0
    owner=$(stat -c %U "$file") group=$(stat -c %G "$file")
    { grep -vF -- "$2" "$file" || true; } | write_file 0600 "$owner" "$group" "$file"
}

# The last root step: Wheelhouse has proven its runner key over the tailnet, so public SSH closes without the open
# tailnet session bootstrap-products.sh asks a person for. The administrator must keep a key of its own first.
phase_seal() {
    local material account
    valid_admin "$1"
    material=$(awk 'NF && $1 !~ /^#/ {print $2; exit}')
    [[ $material =~ ^[A-Za-z0-9+/]+=*$ ]] || { echo 'Provide the one-time public key on stdin.' >&2; exit 2; }
    holds_own_key "$1" "$material" || {
        echo "Administrator $1 has no SSH key besides Wheelhouse's one-time key; add yours before sealing." >&2; exit 1;
    }
    harden_ssh >/dev/null
    read -r state _ <<<"$(tailnet_state)"
    [[ $state == Running ]] && ip link show tailscale0 >/dev/null 2>&1 || {
        echo 'Tailscale is not running; public SSH stays open.' >&2; exit 1;
    }
    ufw allow in on tailscale0 to any port 22 proto tcp comment 'ssh over the tailnet' >/dev/null
    ufw --force delete allow 22/tcp >/dev/null 2>&1 || true
    ufw --force enable >/dev/null
    for account in root "$1" ${SUDO_USER:-}; do
        remove_key "$account" "$material"
    done
    echo 'Keys-only SSH, no root sign-in, public SSH closed, the one-time key removed.'
}

enroll_main() {
    (($#)) || enroll_usage
    require_enrolling_root
    case "$1" in
        admin) [[ $# -eq 2 ]] || enroll_usage; phase_admin "$2" ;;
        packages) [[ $# -eq 1 ]] || enroll_usage; phase_packages ;;
        host) [[ $# -eq 1 ]] || enroll_usage; phase_host ;;
        join) [[ $# -eq 2 ]] || enroll_usage; phase_join "$2" ;;
        accept-runner) [[ $# -eq 1 ]] || enroll_usage; phase_accept_runner ;;
        seal) [[ $# -eq 2 ]] || enroll_usage; phase_seal "$2" ;;
        *) enroll_usage ;;
    esac
}

if [[ ${BASH_SOURCE[0]} == "$0" ]]; then
    enroll_main "$@"
fi
