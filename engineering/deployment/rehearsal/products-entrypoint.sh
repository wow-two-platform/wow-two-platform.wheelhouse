#!/bin/bash
# The local products host: bootstrap-products.sh's own steps lay out the deploy account, the kit, the `platform` network
# and the SSH policy. Packages, the firewall, unattended upgrades and the tailnet stay with a real Ubuntu host.
set -euo pipefail

# Its own Docker daemon; the deploy account joins its group, as the docker group grants on a VPS. A restarted
# container keeps the previous daemon's PID files, which would stop this one.
groupadd -f docker
rm -f /var/run/docker.pid /var/run/docker/containerd/containerd.pid
dind dockerd --host=unix:///var/run/docker.sock --group docker >/var/log/dockerd.log 2>&1 &
for _ in $(seq 60); do docker info >/dev/null 2>&1 && break; sleep 1; done
docker info >/dev/null

# Bundles name their images localhost:15000/...; the daemon here reaches the rig's registry under that same name.
socat TCP-LISTEN:15000,fork,reuseaddr,bind=127.0.0.1 TCP:registry:5000 &

# The products host's tailnet address, which the runner pins. There is no tailnet here, so it sits on loopback.
ip addr add "${PRODUCTS_TAILNET_ADDRESS:?}/32" dev lo 2>/dev/null || true

# A persisted host key keeps the pinned known_hosts valid across container replacement; it is the only host key.
keys=/rehearsal-keys
[ -f "$keys/ssh_host_ed25519_key" ] || ssh-keygen -q -t ed25519 -N '' -C products-host -f "$keys/ssh_host_ed25519_key"
install -m 600 "$keys/ssh_host_ed25519_key" /etc/ssh/ssh_host_ed25519_key
install -m 644 "$keys/ssh_host_ed25519_key.pub" /etc/ssh/ssh_host_ed25519_key.pub
printf 'HostKey /etc/ssh/ssh_host_ed25519_key\n' > /etc/ssh/sshd_config.d/00-rehearsal-host-key.conf

. /kit/bootstrap-products.sh
ensure_deploy_account
lay_out_kit /kit
ensure_platform_network
write_ssh_policy
# Alpine's sshd has no PAM and refuses a locked account; Ubuntu's PAM sign-in takes keys on the locked one.
usermod -p '*' "$deploy_user"
exec /usr/sbin/sshd -D -e
