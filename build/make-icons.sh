#!/usr/bin/env bash
# Generuje ikony aplikacji z logo BPP: build/make-icons.sh [sciezka/logo_bpp.png]
# Domyslne zrodlo: ../bpp/src/bpp/static/bpp/images/logo_bpp.png (repo BPP obok tego repo).
# Wynik (commitowany): installer/assets/logo.png (szer. 480 px), installer/assets/bpp.ico (16-256 px).
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
src="${1:-$repo/../bpp/src/bpp/static/bpp/images/logo_bpp.png}"
if [[ ! -f "$src" ]]; then
  echo "Brak pliku zrodlowego: $src" >&2
  exit 2
fi
src_dir="$(cd "$(dirname "$src")" && pwd)"
src_name="$(basename "$src")"
out="$repo/installer/assets"
mkdir -p "$out"

docker run --rm \
  -v "$src_dir:/in:ro" \
  -v "$out:/out" \
  -e "SRC=/in/$src_name" \
  python:3.12-slim \
  sh -c 'pip install --quiet --root-user-action=ignore --disable-pip-version-check pillow==11.3.0 && python - <<"PY"
import os
from PIL import Image

src = Image.open(os.environ["SRC"]).convert("RGBA")
w = 480
h = round(src.height * w / src.width)
src.resize((w, h), Image.LANCZOS).save("/out/logo.png", optimize=True)

logo = Image.open("/out/logo.png").convert("RGBA")
side = max(logo.size)
square = Image.new("RGBA", (side, side), (0, 0, 0, 0))
square.paste(logo, ((side - logo.width) // 2, (side - logo.height) // 2))
square.save("/out/bpp.ico", sizes=[(s, s) for s in (16, 24, 32, 48, 64, 128, 256)])
PY'

echo "$out/logo.png"
echo "$out/bpp.ico"
