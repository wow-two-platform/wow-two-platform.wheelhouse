#!/usr/bin/env bash
# Run as the host administrator. Gives one Wheelhouse console's runner its own SSH identity for a host: the private key
# exists only inside the console's persistent volume, and its public half is authorized for the deploy account with
# `restrict`. An existing identity for the server ID is never replaced.
#
#   Same host (the control host):  authorize-runner.sh <wheelhouse-dev|wheelhouse-prod> <server-id>
#   Products host, on the control host: authorize-runner.sh wheelhouse-prod <server-id> --remote <tailnet-address>
#       --host-key <file>  creates the identity pinned to that address and prints its public key; run it again once
#       the products host accepts the key, to prove the console reaches it.
#   Products host, on that host:   authorize-runner.sh --accept <runner-public-key-file>
set -euo pipefail

deploy_user=wheelhouse-deploy
authorized_keys=/home/$deploy_user/.ssh/authorized_keys

usage() {
    cat >&2 <<'EOF'
Usage: sudo bash authorize-runner.sh <wheelhouse-dev|wheelhouse-prod> <server-id>
       sudo bash authorize-runner.sh wheelhouse-prod <server-id> --remote <tailnet-address> --host-key <file>
       sudo bash authorize-runner.sh --accept <runner-public-key-file>
EOF
    exit 2
}

[[ $EUID -eq 0 ]] || usage

# One Ed25519 key from a file holding exactly one key line: "ssh-ed25519 <key> [comment]", or, unless the second
# argument is `bare`, a known_hosts line "<hosts> ssh-ed25519 <key>". Prints "ssh-ed25519 <key>".
ed25519_key() {
    local file=$1 shape=${2:-any} key check
    [[ -f $file && ! -L $file ]] || { echo "Missing key file: $file" >&2; exit 1; }
    [[ $(awk 'NF && $1 !~ /^#/ {count++} END {print count + 0}' "$file") == 1 ]] || {
        echo 'Provide exactly one key line.' >&2; exit 1;
    }
    if [[ $shape == bare ]]; then
        key=$(awk 'NF && $1 !~ /^#/ && $1 == "ssh-ed25519" {print $1 " " $2}' "$file")
    else
        key=$(awk 'NF && $1 !~ /^#/ {for (i = 1; i < NF; i++) if ($i == "ssh-ed25519") {print $i " " $(i + 1); exit}}' "$file")
    fi
    [[ $key =~ ^ssh-ed25519\ [A-Za-z0-9+/]+=*$ ]] || {
        echo 'Provide one Ed25519 public key without authorized_keys options.' >&2; exit 1;
    }
    check=$(mktemp)
    printf '%s\n' "$key" > "$check"
    ssh-keygen -l -f "$check" >/dev/null 2>&1 || { rm -f "$check"; echo 'The key does not parse.' >&2; exit 1; }
    rm -f "$check"
    printf '%s\n' "$key"
}

# Products host: authorizes Wheelhouse prod's runner key for the deploy account, once.
accept() {
    local key fingerprint check
    key=$(ed25519_key "$1" bare)
    id "$deploy_user" >/dev/null 2>&1 || { echo 'The deploy account is missing; run bootstrap-products.sh first.' >&2; exit 1; }
    [[ ! -L $authorized_keys ]] || { echo 'Refusing a symlinked authorized_keys.' >&2; exit 1; }
    install -d -m 0700 -o "$deploy_user" -g "$(id -gn "$deploy_user")" "$(dirname "$authorized_keys")"
    touch "$authorized_keys"
    chown "$deploy_user:$(id -gn "$deploy_user")" "$authorized_keys"
    chmod 0600 "$authorized_keys"
    check=$(mktemp)
    printf '%s\n' "$key" > "$check"
    fingerprint=$(ssh-keygen -l -f "$check" | awk '{print $2}')
    rm -f "$check"
    if grep -Fq -- "$key" "$authorized_keys"; then
        echo "Runner key $fingerprint is already authorized for $deploy_user."
        return
    fi
    # Commands and file copies only: no terminal, agent or port forwarding.
    printf 'restrict %s\n' "$key" >> "$authorized_keys"
    echo "Runner key $fingerprint is authorized for $deploy_user with restrict."
}

if [[ ${1:-} == --accept ]]; then
    [[ $# -eq 2 ]] || usage
    accept "$2"
    exit 0
fi

[[ $# -ge 2 && $1 =~ ^wheelhouse-(dev|prod)$ && $2 =~ ^[a-z][a-z0-9-]{0,47}$ ]] || usage
console=$1
container=$1-console-1
server=$2
shift 2
address='' host_key_file=''
while (($#)); do
    case "$1" in
        --remote) address=${2:-}; shift 2 || usage ;;
        --host-key) host_key_file=${2:-}; shift 2 || usage ;;
        *) usage ;;
    esac
done
directory=/data/deployments/ssh/$server

# Runs inside the console as its own user, so every file it writes belongs to the console.
in_console() { docker exec -i "$container" sh -c "$1"; }

if [[ -n $address || -n $host_key_file ]]; then
    [[ -n $address && -n $host_key_file ]] || usage
    [[ $console == wheelhouse-prod ]] || {
        echo "Only Wheelhouse prod deploys products; dev never receives a products host's key." >&2; exit 1;
    }
    # The runner reaches a products host over the tailnet only, by its address: no DNS inside the console.
    python3 -c 'import ipaddress, sys; sys.exit(ipaddress.ip_address(sys.argv[1]) not in ipaddress.ip_network("100.64.0.0/10"))' \
        "$address" 2>/dev/null || { echo 'Use the products host tailnet IPv4 address (100.64.0.0/10).' >&2; exit 1; }
    pin="$address $(ed25519_key "$host_key_file")"
    verify="ssh -i $directory/identity -o UserKnownHostsFile=$directory/known_hosts -o StrictHostKeyChecking=yes \
        -o BatchMode=yes -o IdentitiesOnly=yes -o ConnectTimeout=10 $deploy_user@$address true"
    if in_console "test -e $directory/identity"; then
        [[ $(in_console "cat $directory/known_hosts") == "$pin" ]] || {
            echo "An identity for $server already pins another address or host key in $container; review it." >&2; exit 1;
        }
        if in_console "$verify" >/dev/null 2>&1; then
            echo "Runner identity for $server in $container reaches $deploy_user at $address." >&2
            exit 0
        fi
        echo "The products host does not accept it yet. Accept this key there (authorize-runner.sh --accept), and" \
             "check the tailnet policy lets tag:control reach tag:products on 22:" >&2
        in_console "ssh-keygen -y -f $directory/identity" | cut -d' ' -f1,2
        exit 1
    fi
    work=$(mktemp -d)
    trap 'shred -u "$work/identity" 2>/dev/null || true; rm -rf "$work"' EXIT
    ssh-keygen -q -t ed25519 -N '' -C "$console-runner" -f "$work/identity"
    printf '%s\n' "$pin" > "$work/known_hosts"
    in_console "umask 077; mkdir -p $directory"
    in_console "umask 077; cat > $directory/identity" < "$work/identity"
    in_console "umask 077; cat > $directory/known_hosts" < "$work/known_hosts"
    echo "Runner identity for $server is in $container, pinned to $address. Its public key follows; on the products" \
         "host, save it to a file and run authorize-runner.sh --accept <file>, then run this command again." >&2
    cut -d' ' -f1,2 "$work/identity.pub"
    exit 0
fi

if in_console "test -e $directory/identity"; then
    echo "An identity for $server already exists in $container; review it instead of replacing it." >&2
    exit 1
fi

work=$(mktemp -d)
trap 'shred -u "$work/identity" 2>/dev/null || true; rm -rf "$work"' EXIT
ssh-keygen -q -t ed25519 -N '' -C "$console-runner" -f "$work/identity"
gateway=$(docker network inspect platform -f '{{(index .IPAM.Config 0).Gateway}}')
printf '%s %s\n' "$gateway" "$(cut -d' ' -f1,2 /etc/ssh/ssh_host_ed25519_key.pub)" > "$work/known_hosts"

in_console "umask 077; mkdir -p $directory"
in_console "umask 077; cat > $directory/identity" < "$work/identity"
in_console "umask 077; cat > $directory/known_hosts" < "$work/known_hosts"
printf 'restrict %s\n' "$(cut -d' ' -f1,2 "$work/identity.pub")" >> "$authorized_keys"

in_console "ssh -i $directory/identity -o UserKnownHostsFile=$directory/known_hosts \
    -o StrictHostKeyChecking=yes -o BatchMode=yes $deploy_user@$gateway true"
echo "Runner identity for $server is in $container and reaches $deploy_user at $gateway."
