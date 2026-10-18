#!/usr/bin/env bash
# Uruchamia testy bats distro w kontenerze. Bez argumentow: wszystkie distro/tests/*.bats.
# Repo jest montowane pod ta sama sciezka bezwzgledna co na hoscie (potrzebne
# pozniej do docker-in-docker); cwd = <repo>/distro/rootfs/opt/bpp.
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
image="bpp-distro-tests"

docker build -q -t "$image" "$repo/distro/tests" >/dev/null

if [ "$#" -gt 0 ]; then
  files=()
  for f in "$@"; do
    case "$f" in
      /*) files+=("$f") ;;
      *) files+=("$repo/distro/tests/$(basename "$f")") ;;
    esac
  done
else
  files=("$repo"/distro/tests/*.bats)
fi

exec docker run --rm \
  -v "$repo:$repo" \
  -v /var/run/docker.sock:/var/run/docker.sock \
  -w "$repo/distro/rootfs/opt/bpp" \
  -e BPP_HOME="$repo/distro/rootfs/opt/bpp" \
  "$image" bats "${files[@]}"
