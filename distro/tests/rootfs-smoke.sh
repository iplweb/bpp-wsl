#!/usr/bin/env bash
# Smoke test obrazu rootfs: buduje (--load) i uruchamia dockerd w kontenerze
# --privileged (amd64, emulacja). Test uzywa vfs jako storage-driver (overlay2
# na overlayfs/emulacji nie dziala); dostarczany obraz zostaje przy domyslnych.
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
img="bpp-rootfs-smoke:latest"

docker buildx build --platform linux/amd64 --build-arg BPP_IMAGE_TAG=TESTTAG \
  --load -t "$img" "$repo/distro"

docker run --rm --privileged --platform linux/amd64 "$img" bash -c '
  set -e
  dockerd --storage-driver=vfs >/tmp/dockerd.log 2>&1 &
  for _ in $(seq 60); do docker version >/dev/null 2>&1 && break; sleep 1; done
  docker version >/dev/null || { cat /tmp/dockerd.log; exit 1; }
  docker compose version
  bpp-ctl status
  test -L /etc/systemd/system/multi-user.target.wants/docker.service
  grep -q systemd=true /etc/wsl.conf
  test "$(cat /opt/bpp/image-tag)" = TESTTAG
  test -x /opt/bpp/bin/bpp-ctl
  test ! -e /opt/bpp/state
  echo "rootfs smoke OK"
'
