#!/usr/bin/env bash
# Podpisuje exe przez jsign w kontenerze: build/sign.sh <plik.exe>
# Podpis nalezy wykonac PO BPP.Pack (PayloadArchive toleruje dopisany podpis).
#
# Zmienne (klasyczny keystore / token / PFX):
#   BPP_SIGN_KEYSTORE   sciezka do keystore (PFX/JKS) lub URL PKCS#11 / "NONE"
#   BPP_SIGN_STOREPASS  haslo keystore
#   BPP_SIGN_ALIAS      alias certyfikatu
#   BPP_SIGN_STORETYPE  PKCS12 | JKS | PIV | ... (domyslnie: PKCS12)
# Zmienne (Azure Trusted Signing zamiast powyzszych):
#   BPP_SIGN_AZURE_ENDPOINT, BPP_SIGN_AZURE_ACCOUNT, BPP_SIGN_AZURE_PROFILE,
#   BPP_SIGN_AZURE_TOKEN  (token dostepu z `az account get-access-token`)
set -euo pipefail

# jsign nie ma oficjalnego obrazu Dockera: przypiety jar (wersja + SHA-256,
# policzony przy pierwszym pobraniu z GitHub Releases) w przypietym obrazie JRE.
JSIGN_VERSION=7.6
JSIGN_SHA256=6cf58aa080e7ff82656f320cff8d4a3bff30408df8766806dcba3740dd839fa5
JRE_IMAGE=eclipse-temurin:21.0.8_9-jre
TSA="${BPP_SIGN_TSA:-http://timestamp.digicert.com}"

if [[ $# -ne 1 || ! -f "$1" ]]; then
  echo "Uzycie: $0 <plik.exe>" >&2
  exit 2
fi
file="$(cd "$(dirname "$1")" && pwd)/$(basename "$1")"

missing=()
if [[ -n "${BPP_SIGN_AZURE_ENDPOINT:-}" ]]; then
  for v in BPP_SIGN_AZURE_ACCOUNT BPP_SIGN_AZURE_PROFILE BPP_SIGN_AZURE_TOKEN; do
    [[ -n "${!v:-}" ]] || missing+=("$v")
  done
  mode=azure
else
  for v in BPP_SIGN_KEYSTORE BPP_SIGN_STOREPASS BPP_SIGN_ALIAS; do
    [[ -n "${!v:-}" ]] || missing+=("$v")
  done
  mode=keystore
fi
if ((${#missing[@]})); then
  echo "Blad: podpisywanie wlaczone, ale brak zmiennych: ${missing[*]}" >&2
  echo "Ustaw je (patrz naglowek build/sign.sh i README.md) albo nie ustawiaj BPP_SIGN=1." >&2
  exit 1
fi

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
jar="$repo/dist/tools/jsign-$JSIGN_VERSION.jar"
if [[ ! -f "$jar" ]]; then
  mkdir -p "$(dirname "$jar")"
  curl -fsSL -o "$jar.part" \
    "https://github.com/ebourg/jsign/releases/download/$JSIGN_VERSION/jsign-$JSIGN_VERSION.jar"
  mv "$jar.part" "$jar"
fi
if command -v sha256sum >/dev/null; then
  actual="$(sha256sum "$jar" | cut -d' ' -f1)"
else
  actual="$(shasum -a 256 "$jar" | cut -d' ' -f1)"
fi
if [[ "$actual" != "$JSIGN_SHA256" ]]; then
  echo "Blad: jsign-$JSIGN_VERSION.jar ma zla sume SHA-256 ($actual)" >&2
  rm -f "$jar"
  exit 1
fi

dir="$(dirname "$file")"
name="$(basename "$file")"
args=(--tsaurl "$TSA" --name "BPP" --url "https://github.com/iplweb/bpp")
mounts=(-v "$dir:/work" -v "$jar:/jsign.jar:ro")
envs=()

if [[ $mode == azure ]]; then
  args+=(--storetype TRUSTEDSIGNING --keystore "$BPP_SIGN_AZURE_ENDPOINT"
         --storepass "env:BPP_SIGN_AZURE_TOKEN"
         --alias "$BPP_SIGN_AZURE_ACCOUNT/$BPP_SIGN_AZURE_PROFILE")
  envs+=(-e BPP_SIGN_AZURE_TOKEN)
else
  ks="$BPP_SIGN_KEYSTORE"
  if [[ -f "$ks" ]]; then
    mounts+=(-v "$(cd "$(dirname "$ks")" && pwd)/$(basename "$ks"):/keystore:ro")
    ks=/keystore
  fi
  args+=(--storetype "${BPP_SIGN_STORETYPE:-PKCS12}" --keystore "$ks"
         --storepass "env:BPP_SIGN_STOREPASS" --alias "$BPP_SIGN_ALIAS")
  envs+=(-e BPP_SIGN_STOREPASS)
fi

docker run --rm "${mounts[@]}" "${envs[@]}" "$JRE_IMAGE" java -jar /jsign.jar "${args[@]}" "/work/$name"
echo "Podpisano: $file"
