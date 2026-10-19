#!/usr/bin/env bash
# Reserves the fixed addresses on a host's kit networks: Docker hands out dynamic addresses only from .128/25, so a
# container that starts before Traefik (.2), PostgreSQL (.3) or a tailnet node (.10, .12, .64/26) can never take its
# address. bootstrap.sh and bootstrap-products.sh create the networks that way; this repairs a network created before.
#
#   sudo bash repair-networks.sh            every kit network on this host: `control`, `platform`
#   sudo bash repair-networks.sh --check    only reports which network lacks the reserved range
#
# A network that already reserves the range is left alone, so a rerun changes nothing. One that does not is recreated
# during a short maintenance step: each container is disconnected (its processes keep running), the network is created
# again with the range, and each container reconnects with its aliases, fixed-address ones first at their own address,
# dynamic ones after, now from .128/25. Their traffic on that network pauses for a few seconds. Sourcing this file
# defines repair_network without running it; Wheelhouse's enrollment uses it on a products host.
set -euo pipefail

# name subnet gateway range: the networks the kit creates on either host.
kit_networks=(
    'control 172.30.1.0/24 172.30.1.1 172.30.1.128/25'
    'platform 172.30.0.0/24 172.30.0.1 172.30.0.128/25'
)

# Prints "ok" when a network reserves its range, "missing" when it does not exist, else "repair"; refuses one whose
# subnet or gateway differ, since that is not a range to add but addresses to review.
network_state() {  # name subnet gateway range
    if ! docker network inspect "$1" >/dev/null 2>&1; then
        echo missing
        return
    fi
    docker network inspect "$1" | python3 -c '
import json, sys
network = json.load(sys.stdin)[0]
subnet, gateway, ip_range = sys.argv[1:4]
config = network.get("IPAM", {}).get("Config") or []
if network.get("Driver") != "bridge" or len(config) != 1 or config[0].get("Subnet") != subnet \
        or config[0].get("Gateway") != gateway:
    sys.exit("Network " + network.get("Name", "?") + " differs in subnet or gateway; review its addresses by hand.")
print("ok" if config[0].get("IPRange") == ip_range else "repair")
' "$2" "$3" "$4"
}

# Each attached container as "<name> <fixed address or -> <alias,alias,...>", fixed-address ones first.
attachments() {  # network
    docker network inspect "$1" | python3 -c '
import json, subprocess, sys
name = sys.argv[1]
rows = []
for container in (json.load(sys.stdin)[0].get("Containers") or {}).values():
    inspected = json.loads(subprocess.run(["docker", "inspect", container["Name"]], capture_output=True, text=True,
                                          check=True).stdout)[0]
    endpoint = inspected["NetworkSettings"]["Networks"].get(name) or {}
    fixed = (endpoint.get("IPAMConfig") or {}).get("IPv4Address") or "-"
    aliases = [alias for alias in endpoint.get("Aliases") or []
               if alias not in (inspected["Config"].get("Hostname"), inspected["Id"][:12])]
    rows.append((fixed == "-", container["Name"], fixed, ",".join(aliases) or "-"))
for _, container, fixed, aliases in sorted(rows):
    print(container, fixed, aliases)
' "$1"
}

repair_network() {  # name subnet gateway range
    local name=$1 subnet=$2 gateway=$3 ip_range=$4 state rows container fixed aliases arguments
    state=$(network_state "$name" "$subnet" "$gateway" "$ip_range")
    case $state in
        missing) echo "$name: not on this host"; return ;;
        ok) echo "$name: reserves $ip_range for dynamic addresses"; return ;;
    esac
    rows=$(attachments "$name")
    while read -r container fixed aliases; do
        [[ -n $container ]] && docker network disconnect --force "$name" "$container"
    done <<<"$rows"
    docker network rm "$name" >/dev/null
    docker network create --driver bridge --subnet "$subnet" --ip-range "$ip_range" --gateway "$gateway" "$name" >/dev/null
    while read -r container fixed aliases; do
        [[ -n $container ]] || continue
        arguments=()
        [[ $fixed != - ]] && arguments+=(--ip "$fixed")
        if [[ $aliases != - ]]; then
            IFS=, read -ra list <<<"$aliases"
            for alias in "${list[@]}"; do arguments+=(--alias "$alias"); done
        fi
        docker network connect "${arguments[@]}" "$name" "$container"
    done <<<"$rows"
    echo "$name: recreated with $ip_range for dynamic addresses; $(grep -c . <<<"$rows" || true) container(s) reconnected"
}

repair_main() {
    local check=false entry state
    case "${1:-}" in
        --check) check=true ;;
        '') ;;
        *) echo 'Usage: sudo bash repair-networks.sh [--check]' >&2; exit 2 ;;
    esac
    for entry in "${kit_networks[@]}"; do
        read -r name subnet gateway ip_range <<<"$entry"
        if $check; then
            state=$(network_state "$name" "$subnet" "$gateway" "$ip_range")
            echo "$name: $state"
        else
            repair_network "$name" "$subnet" "$gateway" "$ip_range"
        fi
    done
}

if [[ ${BASH_SOURCE[0]} == "$0" ]]; then
    repair_main "$@"
fi
