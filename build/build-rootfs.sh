#!/usr/bin/env bash
# Buduje rootfs dystrybucji WSL: build/build-rootfs.sh <BPP_IMAGE_TAG>
# Wynik: dist/bpp-rootfs.tar.gz (zwykly tar, gotowy do `wsl --import`).
set -euo pipefail

if [[ $# -ne 1 || -z "$1" ]]; then
  echo "Uzycie: $0 <BPP_IMAGE_TAG>" >&2
  exit 2
fi
tag="$1"

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
mkdir -p "$repo/dist"
out="$repo/dist/bpp-rootfs.tar.gz"
tmp="$out.part"
trap 'rm -f "$tmp"' EXIT

docker buildx build --platform linux/amd64 \
  --build-arg "BPP_IMAGE_TAG=$tag" \
  --output type=tar,dest=- "$repo/distro" | gzip > "$tmp"
mv "$tmp" "$out"
trap - EXIT
echo "$out"
