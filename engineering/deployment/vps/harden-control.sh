#!/usr/bin/env bash
# Hardens the control host like a products host once CI no longer reaches it (vps.md § Host hardening). The owner runs
# it as the administrator, after the self-deploy trial and the retirement of CI's deploy path; never CI, never Wheelhouse.
#
#   sudo bash harden-control.sh --check                       what the default run would change; exits 1 while anything would
#   sudo bash harden-control.sh                               firewall on with public SSH kept, keys-only SSH, security updates
#   sudo bash harden-control.sh --check --tailnet-only-ssh    the same for the closing run
#   sudo bash harden-control.sh --tailnet-only-ssh            also closes public SSH and pins the consoles' runner keys to their
#                                                             network; needs an open SSH session over the tailnet
#   sudo bash harden-control.sh --public-ssh                  reopens public SSH: the fallback from the OVH KVM console
#
# Inbound is denied by default. Allowed: SSH, 443 and 8443 on tailscale0 (the owner and the consoles' Serve ports); SSH
# from the consoles' `control` network (172.30.1.0/24) to the host's `platform` address 172.30.0.1, where their runners
# connect; Tailscale's direct port; public SSH until --tailnet-only-ssh. Workloads on `platform` lose host SSH. Docker
# publishes only on loopback here, so its rules open nothing. Every step converges: a rerun changes nothing. Sourcing
# it defines the steps without running them; the SSH policy, security updates and tailnet checks are bootstrap-products.sh's.
set -euo pipefail

harden_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
# shellcheck source=bootstrap-products.sh
. "$harden_dir/bootstrap-products.sh"

# The control host's own drop-in name; its content is the products host's (write_ssh_policy).
ssh_policy=/etc/ssh/sshd_config.d/01-wheelhouse-control.conf
console_network=172.30.1.0/24
runner_address=172.30.0.1
deploy_keys=$deploy_home/.ssh/authorized_keys
ufw_defaults=/etc/default/ufw
upgrades_policy=/etc/apt/apt.conf.d/52wheelhouse-unattended-upgrades
public_rule="ufw allow 22/tcp comment 'public ssh'"

harden_usage() {
    cat <<'EOF'
Usage: sudo bash harden-control.sh [--check] [--tailnet-only-ssh | --public-ssh]
Hardens the Wheelhouse control host: firewall, keys-only SSH, unattended security updates; vps.md § Host hardening.
  --check             Report what would change, changing nothing; exits 1 while anything would.
  --tailnet-only-ssh  Close public SSH and pin the consoles' runner keys; needs an open SSH session over the tailnet.
  --public-ssh        Reopen public SSH: the fallback when the tailnet path is lost (from the OVH KVM console).
EOF
}

harden_arguments() {
    check_only=false ssh_mode=keep
    while (($#)); do
        case "$1" in
            --check) check_only=true; shift ;;
            --tailnet-only-ssh) [[ $ssh_mode == keep ]] || { harden_usage >&2; exit 2; }; ssh_mode=tailnet; shift ;;
            --public-ssh) [[ $ssh_mode == keep ]] || { harden_usage >&2; exit 2; }; ssh_mode=public; shift ;;
            --help|-h) harden_usage; exit 0 ;;
            *) harden_usage >&2; exit 2 ;;
        esac
    done
}

# The control host's own marks: its consoles' network and the deploy account. A products host has neither.
require_control_host() {
    docker network inspect control >/dev/null 2>&1 && id "$deploy_user" >/dev/null 2>&1 || {
        echo 'This is not a Wheelhouse control host: the `control` network or the deploy account is missing.' >&2
        exit 1
    }
}

# Every rule this host keeps, as `ufw show added` lists it; public SSH follows the mode and the firewall's state.
wanted_rules() {  # public: yes | no
    printf '%s\n' \
        "ufw allow in on tailscale0 to any port 22 proto tcp comment 'ssh over the tailnet'" \
        "ufw allow in on tailscale0 to any port 443,8443 proto tcp comment 'consoles over the tailnet'" \
        "ufw allow from $console_network to $runner_address port 22 proto tcp comment 'console runners'" \
        "ufw allow 41641/udp comment 'tailscale direct'"
    [[ $1 == yes ]] && printf '%s\n' "$public_rule"
    return 0
}

firewall_active() {
    ufw status | grep -qx 'Status: active'
}

# Whether public SSH stays: --public-ssh opens it, --tailnet-only-ssh closes it, a plain run keeps what is there (open
# while the firewall is still off, since enabling it must never cut the session running this).
public_ssh() {
    case $ssh_mode in
        public) echo yes ;;
        tailnet) echo no ;;
        *) if ! firewall_active || ufw show added | grep -qxF -- "$public_rule"; then echo yes; else echo no; fi ;;
    esac
}

# The deploy account's key lines that a console runner key could be: `restrict` without a source pin.
unpinned_keys() {
    [[ -f $deploy_keys ]] || return 0
    grep -E '^restrict ssh-' "$deploy_keys" || true
}

# One line per item: "ok <item>" or "change <item>"; returns 1 when anything would change.
report() {  # public: yes | no
    local pending=0 added effective rule
    added=$(ufw show added 2>/dev/null || true)
    if firewall_active; then echo 'ok      firewall active'; else echo 'change  firewall: enable it'; pending=1; fi
    if grep -qx 'DEFAULT_INPUT_POLICY="DROP"' "$ufw_defaults" && grep -qx 'DEFAULT_OUTPUT_POLICY="ACCEPT"' "$ufw_defaults"; then
        echo 'ok      default deny in, allow out'
    else
        echo 'change  default policy: deny in, allow out'; pending=1
    fi
    while IFS= read -r rule; do
        if grep -qxF -- "$rule" <<<"$added"; then echo "ok      ${rule#ufw }"; else echo "change  add: ${rule#ufw }"; pending=1; fi
    done < <(wanted_rules "$1")
    if [[ $1 == no ]] && grep -qxF -- "$public_rule" <<<"$added"; then
        echo "change  remove: ${public_rule#ufw }"; pending=1
    fi
    while IFS= read -r rule; do
        [[ -z $rule ]] || grep -qxF -- "$rule" < <(wanted_rules yes) || echo "note    other rule kept: ${rule#ufw }"
    done < <(grep '^ufw ' <<<"$added" || true)
    effective=$(sshd -T 2>/dev/null || true)
    for setting in 'passwordauthentication no' 'kbdinteractiveauthentication no' 'permitrootlogin no'; do
        if grep -qx "$setting" <<<"$effective"; then echo "ok      sshd $setting"; else echo "change  sshd $setting"; pending=1; fi
    done
    if [[ -f $upgrades_policy ]] && grep -q 'Automatic-Reboot "false"' "$upgrades_policy"; then
        echo 'ok      unattended security updates, no reboots'
    else
        echo 'change  unattended security updates, no reboots'; pending=1
    fi
    if [[ $ssh_mode == tailnet ]]; then
        local count
        count=$(unpinned_keys | grep -c . || true)
        if ((count == 0)); then echo "ok      runner keys pinned to $console_network"; else
            echo "change  pin $count runner key(s) to $console_network"; pending=1; fi
    fi
    return "$pending"
}

# Closing public SSH needs the tailnet up and an SSH session already open over it, as on a products host.
require_tailnet_session() {
    local state
    read -r state _ <<<"$(tailnet_state)"
    [[ $state == Running ]] && ip link show tailscale0 >/dev/null 2>&1 || {
        echo 'Tailscale is not running; public SSH stays open.' >&2; exit 1;
    }
    ss -Htn state established '( sport = :22 )' | tailnet_session || {
        echo 'Open an SSH session over the tailnet first and keep it open; public SSH stays open.' >&2; exit 1;
    }
}

apply_firewall() {  # public: yes | no
    local rule
    ufw default deny incoming >/dev/null
    ufw default allow outgoing >/dev/null
    # Each rule is one of wanted_rules' fixed lines; eval only splits its quoted comment.
    while IFS= read -r rule; do
        eval "$rule" >/dev/null
    done < <(wanted_rules "$1")
    if [[ $1 == no ]]; then
        ufw --force delete allow 22/tcp >/dev/null 2>&1 || true
    fi
    ufw --force enable >/dev/null
}

# A console runner key works only from the consoles' network: `restrict` gains from="172.30.1.0/24". Other lines stay.
pin_runner_keys() {
    [[ -f $deploy_keys && ! -L $deploy_keys ]] || return 0
    local temporary
    temporary=$(mktemp "$(dirname "$deploy_keys")/.keys.XXXXXX")
    sed -E "s|^restrict (ssh-)|restrict,from=\"$console_network\" \\1|" "$deploy_keys" > "$temporary"
    chown "$deploy_user:$(id -gn "$deploy_user")" "$temporary"
    chmod 0600 "$temporary"
    mv -f "$temporary" "$deploy_keys"
}

harden_main() {
    harden_arguments "$@"
    [[ $EUID -eq 0 ]] || { harden_usage >&2; exit 2; }
    harden_run
}

harden_run() {
    require_control_host
    command -v ufw >/dev/null 2>&1 || {
        if $check_only; then echo 'change  install ufw'; exit 1; fi
        DEBIAN_FRONTEND=noninteractive apt-get install -y --no-upgrade ufw unattended-upgrades
    }
    local public
    public=$(public_ssh)
    if $check_only; then
        report "$public"
        return
    fi
    [[ $ssh_mode != tailnet ]] || require_tailnet_session
    harden_ssh
    enable_security_upgrades
    apply_firewall "$public"
    [[ $ssh_mode != tailnet ]] || pin_runner_keys
    report "$public" || true
    ufw status verbose
}

if [[ ${BASH_SOURCE[0]} == "$0" ]]; then
    harden_main "$@"
fi
