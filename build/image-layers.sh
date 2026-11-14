#!/usr/bin/env bash
# Lists the layers of every image the distro pulls: build/image-layers.sh <BPP_IMAGE_TAG>
# Output (stdout), one line per layer of the linux/amd64 image:
#   <image>TAB<layer digest>TAB<compressed size in bytes>
# build-rootfs.sh bakes it into the rootfs as /opt/bpp/image-layers.tsv, so `bpp-ctl init` can say
# "10 MB z 300 MB" from the first second: Docker itself reveals a layer's size only when it starts
# downloading that layer.
set -euo pipefail

if [[ $# -ne 1 || -z "$1" ]]; then
  echo "Usage: $0 <BPP_IMAGE_TAG>" >&2
  exit 2
fi
tag="$1"
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
compose="$repo/distro/rootfs/opt/bpp/docker-compose.yml"

# Image references straight from the compose file; the only variable in them is BPP_IMAGE_TAG.
mapfile -t images < <(sed -nE 's/^[[:space:]]*image:[[:space:]]*(.*[^[:space:]])[[:space:]]*$/\1/p' "$compose" |
  sed -E "s/\\$\\{BPP_IMAGE_TAG[^}]*\\}/$tag/" | awk '!seen[$0]++')
if [[ ${#images[@]} -eq 0 ]]; then
  echo "No images found in $compose" >&2
  exit 1
fi

for image in "${images[@]}"; do
  if [[ "$image" == *'$'* ]]; then
    echo "Unresolved variable in image reference: $image" >&2
    exit 1
  fi
  # A multi-platform image gives an array with one entry per platform, a single-platform one an object.
  manifest="$(docker manifest inspect -v "$image")"
  lines="$(jq -r --arg image "$image" '
    (if type == "array"
       then map(select(.Descriptor.platform.os == "linux" and .Descriptor.platform.architecture == "amd64"))[0]
       else . end) as $m
    | ($m.OCIManifest // $m.SchemaV2Manifest // {}).layers // []
    | .[] | [$image, .digest, .size] | @tsv' <<<"$manifest")"
  if [[ -z "$lines" ]]; then
    echo "No linux/amd64 layers found for $image" >&2
    exit 1
  fi
  printf '%s\n' "$lines"
done
