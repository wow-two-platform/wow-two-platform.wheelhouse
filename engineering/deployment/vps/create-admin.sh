#!/usr/bin/env bash
# Run once, as root, on a provider image that delivers only `root` (Contabo, for one). Creates the named administrator
# that bootstrap-products.sh needs: a sudo user who signs in with the SSH keys root already trusts. Verify the host key
# in the provider's console, then put your own key on root first (`ssh-copy-id root@<public-ip>`).
# It never sets a password: it prints the one `passwd` command to run yourself, for provider-console recovery.
# Every step converges, so a rerun is safe: a user that already holds root's keys is left as it is, and one that holds
# other keys is refused, never overwritten. Sourcing it defines the steps without running them.
set -euo pipefail
umask 077

deploy_user=wheelhouse-deploy

usage() {
    cat >&2 <<'EOF'
Usage: bash create-admin.sh <admin-user>     (as root, once, on a root-only image)
Creates <admin-user> in the sudo group with root's authorized SSH keys. Sets no password.
EOF
    exit 2
}

die() {
    echo "$*" >&2
    exit 1
}

parse_arguments() {
    [[ $# -eq 1 && $1 =~ ^[a-z][a-z0-9-]{0,30}$ ]] || usage
    [[ $1 != root && $1 != "$deploy_user" ]] || {
        echo "Name the administrator something other than root or $deploy_user." >&2; exit 2;
    }
    admin=$1
}

require_root() {
    [[ $EUID -eq 0 ]] || { echo 'Run as root, once, on the fresh image.' >&2; exit 2; }
}

# The administrator signs in over SSH and reaches root through sudo, so both must exist.
have_sudo() {
    command -v sudo >/dev/null 2>&1 && getent group sudo >/dev/null
}

# The key lines of an authorized_keys file as they stand: no blank lines, comments or carriage returns.
key_lines() {
    tr -d '\r' < "$1" | awk 'NF && $1 !~ /^#/'
}

# How a user's authorized_keys relates to root's: missing (absent or no key), same (the same keys) or different.
keys_relation() {
    local root_file=$1 user_file=$2
    if [[ ! -f $user_file || -z $(key_lines "$user_file") ]]; then
        echo missing
    elif [[ $(key_lines "$root_file" | sort -u) == "$(key_lines "$user_file" | sort -u)" ]]; then
        echo same
    else
        echo different
    fi
}

# Everything is checked before anything changes, so a refusal leaves the host as it was.
create_admin() {
    local root_home root_keys home group relation=missing password_state
    have_sudo || die 'Install sudo first (apt-get install sudo): the administrator reaches root through it.'
    root_home=$(getent passwd root | cut -d: -f6)
    root_keys=$root_home/.ssh/authorized_keys
    [[ -f $root_keys && ! -L $root_keys && -n $(key_lines "$root_keys") ]] || die \
        "Root has no SSH key in $root_keys. From your machine run: ssh-copy-id root@<public-ip>. The administrator copies root's keys, and password sign-in is turned off later."

    if getent passwd "$admin" >/dev/null; then
        (($(id -u "$admin") >= 1000)) || die "$admin is a system account; choose another name."
        home=$(getent passwd "$admin" | cut -d: -f6)
        [[ -d $home && ! -L $home && ! -L $home/.ssh && ! -L $home/.ssh/authorized_keys ]] || die \
            "Refusing $admin: its home or SSH directory is missing or a symlink."
        relation=$(keys_relation "$root_keys" "$home/.ssh/authorized_keys")
        [[ $relation != different ]] || die \
            "$admin already exists with SSH keys that differ from root's. Review $home/.ssh/authorized_keys, or choose another name."
    else
        useradd --create-home --shell /bin/bash "$admin"
        home=$(getent passwd "$admin" | cut -d: -f6)
    fi

    usermod --append --groups sudo "$admin"
    group=$(id -gn "$admin")
    install -d -m 0700 -o "$admin" -g "$group" "$home/.ssh"
    key_lines "$root_keys" | install -m 0600 -o "$admin" -g "$group" /dev/stdin "$home/.ssh/authorized_keys"

    password_state=$(passwd --status "$admin" | awk '{print $2}')
    echo "Administrator $admin is ready: in the sudo group, signing in with the SSH keys root trusts."
    if [[ $password_state == P ]]; then
        echo "It already has a password; to replace it: passwd $admin"
    else
        echo 'It has no password, and this script never sets one. Set it yourself, now, in this root session; sudo and the'
        echo "provider's console sign-in need it:"
        echo "  passwd $admin"
    fi
    echo "Then, before closing this session, sign in from your machine with your key: ssh $admin@<public-ip>"
}

main() {
    parse_arguments "$@"
    require_root
    create_admin
}

if [[ ${BASH_SOURCE[0]} == "$0" ]]; then
    main "$@"
fi
