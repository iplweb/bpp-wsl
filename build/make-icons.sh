#!/usr/bin/env bash
# Generuje ikony aplikacji z logo BPP: build/make-icons.sh [sciezka/logo_bpp.png]
# Domyslne zrodlo: ../bpp/src/bpp/static/bpp/images/logo_bpp.png (repo BPP obok tego repo).
# Wynik (commitowany): installer/assets/logo.png (szer. 480 px, cale logo) oraz installer/assets/bpp.ico
# (16-256 px): tylko pomaranczowa grafika dokumentu z lewej strony logo, bez napisu, na przezroczystym kwadracie.
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

# Icon: only the document graphic = the first run of non-transparent columns from the left
# (the text starts after a fully transparent gap), trimmed to its alpha bounding box.
alpha = src.getchannel("A")
column_has_ink = [alpha.crop((x, 0, x + 1, src.height)).getbbox() is not None for x in range(src.width)]
first = column_has_ink.index(True)
end = column_has_ink.index(False, first) if False in column_has_ink[first:] else src.width
graphic = src.crop((first, 0, end, src.height))
graphic = graphic.crop(graphic.getchannel("A").getbbox())
side = max(graphic.size)
square = Image.new("RGBA", (side, side), (0, 0, 0, 0))
square.paste(graphic, ((side - graphic.width) // 2, (side - graphic.height) // 2))
square.save("/out/bpp.ico", sizes=[(s, s) for s in (16, 24, 32, 48, 64, 128, 256)])
PY'

echo "$out/logo.png"
echo "$out/bpp.ico"
