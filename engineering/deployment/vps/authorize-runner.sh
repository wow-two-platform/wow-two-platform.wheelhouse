#!/usr/bin/env bash
# Run as the host administrator. Gives one Wheelhouse console's runner its own SSH identity for this host: the
# private key exists only inside the console's persistent volume, and its public half is authorized for the deploy
# account with `restrict`. An existing identity for the server ID is never replaced.
set -euo pipefail

usage() {
    echo 'Usage: sudo bash authorize-runner.sh <wheelhouse-dev|wheelhouse-prod> <server-id>' >&2
    exit 2
}

[[ $# -eq 2 && $EUID -eq 0 && $1 =~ ^wheelhouse-(dev|prod)$ && $2 =~ ^[a-z][a-z0-9-]{0,47}$ ]] || usage
container=$1-console-1
server=$2
deploy_user=wheelhouse-deploy
authorized_keys=/home/$deploy_user/.ssh/authorized_keys
directory=/data/deployments/ssh/$server

# Runs inside the console as its own user, so every file it writes belongs to the console.
in_console() { docker exec -i "$container" sh -c "$1"; }

if in_console "test -e $directory/identity"; then
    echo "An identity for $server already exists in $container; review it instead of replacing it." >&2
    exit 1
fi

work=$(mktemp -d)
trap 'shred -u "$work/identity" 2>/dev/null || true; rm -rf "$work"' EXIT
ssh-keygen -q -t ed25519 -N '' -C "$1-runner" -f "$work/identity"
gateway=$(docker network inspect platform -f '{{(index .IPAM.Config 0).Gateway}}')
printf '%s %s\n' "$gateway" "$(cut -d' ' -f1,2 /etc/ssh/ssh_host_ed25519_key.pub)" > "$work/known_hosts"

in_console "umask 077; mkdir -p $directory"
in_console "umask 077; cat > $directory/identity" < "$work/identity"
in_console "umask 077; cat > $directory/known_hosts" < "$work/known_hosts"
printf 'restrict %s\n' "$(cut -d' ' -f1,2 "$work/identity.pub")" >> "$authorized_keys"

in_console "ssh -i $directory/identity -o UserKnownHostsFile=$directory/known_hosts \
    -o StrictHostKeyChecking=yes -o BatchMode=yes $deploy_user@$gateway true"
echo "Runner identity for $server is in $container and reaches $deploy_user at $gateway."
