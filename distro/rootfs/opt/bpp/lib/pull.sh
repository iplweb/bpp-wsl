# shellcheck shell=bash
# Pobieranie obrazow z postepem (krok "pull").

_PULL_LIB_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source-path=SCRIPTDIR source=events.sh
source "$_PULL_LIB_DIR/events.sh"
# shellcheck source-path=SCRIPTDIR source=state.sh
source "$_PULL_LIB_DIR/state.sh"

# Socket Dockera; gdy go nie ma (np. E2E na macOS) - fallback na `docker pull`.
BPP_DOCKER_SOCK="${BPP_DOCKER_SOCK:-/var/run/docker.sock}"

# Program jq: strumien JSON-ow z POST /images/create -> linie "P <ulamek>" / "ERR <tekst>".
# Docker zapowiada wszystkie warstwy obrazu od razu ("Pulling fs layer"), ale rozmiar warstwy podaje dopiero,
# gdy zaczyna ja pobierac (3 naraz). Dlatego licza sie WSZYSTKIE zapowiedziane warstwy: warstwa o jeszcze
# nieznanym rozmiarze wazy tyle, co srednia znanych. (Liczenie tylko znanych dawalo 100% po pierwszej malej
# warstwie i pasek stal tak do konca obrazu.) Pobranie warstwy to 90% jej wagi, rozpakowanie - reszta.
# Stan warstwy: c/t - pobrane/wszystkie bajty (t=0: rozmiar nieznany), d - pobrana, x - rozpakowana.
# shellcheck disable=SC2016 # to program jq, nie ekspansja shella
_PULL_JQ='
foreach inputs as $e ({l: {}};
  if ($e.error // null) != null then .err = $e.error
  else
    ($e.id // "") as $id | ($e.status // "") as $s
    | if $id == "" then .
      elif $s == "Downloading" and ($e.progressDetail.total // 0) > 0
      then .l[$id] = ((.l[$id] // {}) + {c: ($e.progressDetail.current // 0), t: $e.progressDetail.total})
      elif ($s | IN("Pull complete", "Already exists"))
      then .l[$id] = ((.l[$id] // {c: 0, t: 0}) + {d: true, x: true})
      elif ($s | IN("Download complete", "Verifying Checksum", "Extracting"))
      then .l[$id] = ((.l[$id] // {c: 0, t: 0}) + {d: true})
      elif ($s | IN("Pulling fs layer", "Waiting")) and .l[$id] == null
      then .l[$id] = {c: 0, t: 0}
      else . end
  end;
  if .err then "ERR \(.err)"
  else [.l[]] as $L
    | [$L[] | select(.t > 0) | .t] as $known
    | (if ($known | length) > 0 then ($known | add) / ($known | length) else 1 end) as $avg
    | ([$L[] | if .t > 0 then .t else $avg end] | add // 0) as $total
    | ([$L[] | (if .t > 0 then .t else $avg end) as $w
        | if .x then $w elif .d then 0.9 * $w else 0.9 * ([.c, $w] | min) end] | add // 0) as $done
    | if $total > 0 then "P \($done / $total)" else "P 0" end
  end)'

_pull_use_socket() {
  [ -S "$BPP_DOCKER_SOCK" ] || return 1
  command -v curl >/dev/null 2>&1 || return 1
  curl --help all 2>/dev/null | grep -q -- --unix-socket
}

# _pull_ratio <i> <n> <frac> -> (i+frac)/n z 4 miejscami
_pull_ratio() {
  LC_NUMERIC=C awk -v i="$1" -v n="$2" -v f="$3" 'BEGIN { printf "%.4f", (i + f) / n }'
}

# _pull_msg <image> <i> <n> -> "Pobieranie obrazu <image> (i+1 z n)"
_pull_msg() {
  echo "Pobieranie obrazu $1 ($(($2 + 1)) z $3)"
}

# _pull_fmt <frac> -> ulamek z 4 miejscami (pole "item" zdarzenia)
_pull_fmt() {
  LC_NUMERIC=C awk -v f="$1" 'BEGIN { printf "%.4f", f }'
}

# _pull_image_socket <image> <i> <n>
_pull_image_socket() {
  local img="$1" i="$2" n="$3" repo tag
  repo="${img%:*}"
  tag="${img##*:}"
  # brak tagu (ostatni ':' nalezy do portu rejestru albo go nie ma)
  if [ "$repo" = "$img" ] || [[ "$tag" == */* ]]; then
    repo="$img"
    tag=latest
  fi
  curl -sS --fail -N --unix-socket "$BPP_DOCKER_SOCK" -X POST \
    "http://localhost/images/create?fromImage=${repo}&tag=${tag}" |
    jq -rn --unbuffered "$_PULL_JQ" |
    _pull_consume "$img" "$i" "$n"
}

# _pull_consume <image> <i> <n> - czyta "P <ulamek>"/"ERR <tekst>" ze stdin i emituje
# postep co >= BPP_PULL_EMIT_INTERVAL s. Postep jest monotoniczny (ulamek spada,
# gdy pojawiaja sie nowe warstwy - nie pokazujemy cofniecia).
_pull_consume() {
  local img="$1" i="$2" n="$3" last=-1 maxf=0 kind rest
  local every="${BPP_PULL_EMIT_INTERVAL:-1}"
  while read -r kind rest; do
    if [ "$kind" = ERR ]; then
      echo "docker pull $img: $rest" >&2
      return 1
    fi
    maxf="$(LC_NUMERIC=C awk -v a="$maxf" -v b="$rest" 'BEGIN { print (b > a ? b : a) }')"
    if ((SECONDS - last >= every)); then
      last=$SECONDS
      emit pull "$(_pull_ratio "$i" "$n" "$maxf")" "$(_pull_msg "$img" "$i" "$n")" "$(_pull_fmt "$maxf")"
    fi
  done
}

# pull_with_progress - pobiera kazdy obraz z `docker compose config --images`.
pull_with_progress() {
  local -a images
  local n i=0 img use_socket=0 list
  # Podstawienie polecenia (nie <(...)): blad compose musi przerwac pobieranie.
  list="$(bpp_compose config --images)" || return 1
  mapfile -t images < <(awk 'NF && !seen[$0]++' <<<"$list")
  n="${#images[@]}"
  [ "$n" -gt 0 ] || {
    echo "brak obrazow do pobrania" >&2
    return 1
  }
  _pull_use_socket && use_socket=1
  for img in "${images[@]}"; do
    # tagi sa przypiete, wiec obraz obecny lokalnie jest wlasciwy: ponowienie po nieudanym
    # starcie (albo drugi init) nie pobiera niczego od nowa
    if docker image inspect "$img" >/dev/null 2>&1; then
      i=$((i + 1))
      emit pull "$(_pull_ratio "$i" "$n" 0)" "Obraz $img jest już pobrany ($i z $n)" 1
      continue
    fi
    emit pull "$(_pull_ratio "$i" "$n" 0)" "$(_pull_msg "$img" "$i" "$n")" 0
    if [ "$use_socket" = 1 ]; then
      _pull_image_socket "$img" "$i" "$n" || return 1
    else
      docker pull "$img" >&2 || return 1
    fi
    i=$((i + 1))
    emit pull "$(_pull_ratio "$i" "$n" 0)" "Pobrano obraz $img ($i z $n)" 1
  done
}
