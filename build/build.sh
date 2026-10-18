#!/usr/bin/env bash
# Pelny build: build/build.sh <BPP_IMAGE_TAG> <wersja>  ->  dist/BPP-Setup-<wersja>.exe
# BPP_SIGN=1 wlacza podpis (build/sign.sh, zmienne BPP_SIGN_*).
set -euo pipefail

if [[ $# -ne 2 || -z "$1" || -z "$2" ]]; then
  echo "Uzycie: $0 <BPP_IMAGE_TAG> <wersja>   (np. $0 202609.1402 0.1.0)" >&2
  exit 2
fi
tag="$1"
version="$2"
if [[ ! "$version" =~ ^[0-9A-Za-z][0-9A-Za-z._-]*$ ]]; then
  echo "Nieprawidlowa wersja: $version" >&2
  exit 2
fi

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dist="$repo/dist"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
if [[ -z "${DOTNET_ROOT:-}" && -d /opt/homebrew/opt/dotnet/libexec ]]; then
  export DOTNET_ROOT=/opt/homebrew/opt/dotnet/libexec
fi

publish=(-c Release -r win-x64 --self-contained
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true
  -p:EnableWindowsTargeting=true)

echo "==> 1/4 rootfs (tag obrazow: $tag)"
"$repo/build/build-rootfs.sh" "$tag"

echo "==> 2/4 publish BPP.exe i BPP-Setup.exe"
rm -rf "$dist/tray" "$dist/setup"
dotnet publish "$repo/installer/src/BPP.Tray" "${publish[@]}" -o "$dist/tray"
dotnet publish "$repo/installer/src/BPP.Setup" "${publish[@]}" -o "$dist/setup"

echo "==> 3/4 doklejanie ladunku"
out="$dist/BPP-Setup-$version.exe"
dotnet run -c Release --project "$repo/installer/src/BPP.Pack" -- \
  "$dist/setup/BPP-Setup.exe" "$out" \
  "rootfs=$dist/bpp-rootfs.tar.gz" "BPP.exe=$dist/tray/BPP.exe"

echo "==> 4/4 podpis"
if [[ "${BPP_SIGN:-0}" == 1 ]]; then
  "$repo/build/sign.sh" "$out"
else
  echo "Podpis pominiety (ustaw BPP_SIGN=1 i zmienne BPP_SIGN_*, aby podpisac)."
fi

echo "$out"
if size="$(stat -f %z "$out" 2>/dev/null)"; then :; else size="$(stat -c %s "$out")"; fi
echo "Rozmiar: $size B"
