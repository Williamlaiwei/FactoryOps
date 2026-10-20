#!/usr/bin/env bash
# Install Docker only; do not upgrade the host or restart existing applications.
set -euo pipefail
if [ "$(id -u)" != 0 ]; then echo 'Run as root.' >&2; exit 1; fi
. /etc/os-release
if [ "$ID" != ubuntu ] || [ "$(dpkg --print-architecture)" != amd64 ]; then
  echo 'This preparation script supports Ubuntu amd64 only.' >&2; exit 1
fi
if command -v docker >/dev/null 2>&1; then
  docker --version
  docker compose version
  exit 0
fi
export DEBIAN_FRONTEND=noninteractive
export NEEDRESTART_MODE=l
apt-get update -qq
apt-get install -y --no-install-recommends ca-certificates curl
install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
chmod a+r /etc/apt/keyrings/docker.asc
cat > /etc/apt/sources.list.d/docker.sources <<EOF
Types: deb
URIs: https://download.docker.com/linux/ubuntu
Suites: ${UBUNTU_CODENAME:-$VERSION_CODENAME}
Components: stable
Architectures: amd64
Signed-By: /etc/apt/keyrings/docker.asc
EOF
apt-get update -qq
apt-get install -y --no-install-recommends docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
systemctl enable --now docker
docker --version
docker compose version
systemctl is-active scribd-catalog.service || true
