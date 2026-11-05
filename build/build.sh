#!/usr/bin/env bash
# Pelny build: build/build.sh <BPP_IMAGE_TAG> <wersja>  ->  w dist/ trzy pliki wydania:
#   BPP-Setup-<wersja>.exe      mini instalator (.NET Framework 4.8, kilkadziesiat KB) — to pobiera uzytkownik,
#   BPP-<wersja>.exe            wlasciwy program (tray + instalator, .NET 10, ~50 MB) — pobiera go mini instalator,
#   bpp-rootfs-<wersja>.tar.gz  dystrybucja WSL (~170 MB) — pobiera ja BPP.exe po sprawdzeniu wymagan,
# plus SHA256SUMS.txt. Adresy i sumy SHA-256 sa wkompilowane lancuchowo: rootfs -> BPP.exe -> BPP-Setup.exe,
# wiec wszystkie trzy pliki musza trafic do wydania v<wersja> w repo BPP_GITHUB_REPO (domyslnie iplweb/bpp-wsl).
# BPP_SIGN=1 wlacza podpis (build/sign.sh, zmienne BPP_SIGN_*): BPP.exe jest podpisywany PRZED policzeniem
# jego sumy (podpis zmienia plik), BPP-Setup.exe na koncu.
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
# Wersja numeryczna dla -p:Version (AssemblyVersion -> wersja w instalatorze i w "Aplikacje"):
# czesc przed pierwszym '-'/'+', postaci N.N.N (InstallerFlow pokazuje 3 skladniki), kazda liczba <= 65535.
numeric_version="${version%%[-+]*}"
if [[ ! "$numeric_version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "Wersja musi miec postac N.N.N z opcjonalnym sufiksem (np. 0.1.0 lub 0.1.0-rc1): $version" >&2
  exit 2
fi
IFS=. read -r -a _parts <<<"$numeric_version"
for _p in "${_parts[@]}"; do
  if ((10#$_p > 65535)); then
    echo "Skladnik wersji wiekszy niz 65535: $version" >&2
    exit 2
  fi
done

case "${BPP_SIGN:-}" in
  1|true|yes) sign=1 ;;
  ""|0|false|no) sign=0 ;;
  *) echo "Nieprawidlowa wartosc BPP_SIGN='$BPP_SIGN' (dozwolone: 1/true/yes lub puste/0/false/no)." >&2; exit 2 ;;
esac

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dist="$repo/dist"
github_repo="${BPP_GITHUB_REPO:-iplweb/bpp-wsl}"
if [[ ! "$github_repo" =~ ^[A-Za-z0-9._-]+/[A-Za-z0-9._-]+$ ]]; then
  echo "Nieprawidlowe BPP_GITHUB_REPO: $github_repo (oczekiwano wlasciciel/repo)" >&2
  exit 2
fi
base_url="https://github.com/$github_repo/releases/download/v$version"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
if [[ -z "${DOTNET_ROOT:-}" && -d /opt/homebrew/opt/dotnet/libexec ]]; then
  export DOTNET_ROOT=/opt/homebrew/opt/dotnet/libexec
fi

sha256() {
  if command -v sha256sum >/dev/null; then sha256sum "$1" | cut -d' ' -f1; else shasum -a 256 "$1" | cut -d' ' -f1; fi
}

sign_file() {
  if [[ $sign == 1 ]]; then "$repo/build/sign.sh" "$1"; fi
}

rootfs_name="bpp-rootfs-$version.tar.gz"
app_name="BPP-$version.exe"
setup_name="BPP-Setup-$version.exe"
rm -f "$dist/$rootfs_name" "$dist/$app_name" "$dist/$setup_name" "$dist/SHA256SUMS.txt"

echo "==> 1/4 rootfs (tag obrazow: $tag)"
"$repo/build/build-rootfs.sh" "$tag"
mv "$dist/bpp-rootfs.tar.gz" "$dist/$rootfs_name"
rootfs_sha="$(sha256 "$dist/$rootfs_name")"

echo "==> 2/4 BPP.exe (tray + instalator; rootfs: $base_url/$rootfs_name)"
rm -rf "$dist/app"
dotnet publish "$repo/installer/src/BPP.Tray" -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true \
  -p:EnableWindowsTargeting=true -p:Version="$numeric_version" \
  -p:RootfsUrl="$base_url/$rootfs_name" -p:RootfsSha256="$rootfs_sha" \
  -o "$dist/app"
cp "$dist/app/BPP.exe" "$dist/$app_name"
sign_file "$dist/$app_name"
app_sha="$(sha256 "$dist/$app_name")"

echo "==> 3/4 mini instalator BPP-Setup.exe (.NET Framework 4.8)"
rm -rf "$dist/setup"
dotnet build "$repo/installer/src/BPP.Bootstrap" -c Release -p:Version="$numeric_version" \
  -p:AppUrl="$base_url/$app_name" -p:AppSha256="$app_sha" -p:AppFileName="$app_name" \
  -p:RootfsFileName="$rootfs_name" -o "$dist/setup"
cp "$dist/setup/BPP-Setup.exe" "$dist/$setup_name"
sign_file "$dist/$setup_name"

echo "==> 4/4 sumy kontrolne"
if [[ $sign == 0 ]]; then echo "Podpis pominiety (ustaw BPP_SIGN=1 i zmienne BPP_SIGN_*, aby podpisac)."; fi
(cd "$dist" && for f in "$setup_name" "$app_name" "$rootfs_name"; do echo "$(sha256 "$f")  $f"; done > SHA256SUMS.txt)
cat "$dist/SHA256SUMS.txt"
for f in "$setup_name" "$app_name" "$rootfs_name"; do
  echo "$dist/$f  $(wc -c < "$dist/$f" | tr -d ' ') B"
done
