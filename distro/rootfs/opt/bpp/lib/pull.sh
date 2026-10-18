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
# Ulamek = suma pobranych bajtow / suma bajtow wszystkich dotad widzianych warstw.
# shellcheck disable=SC2016 # to program jq, nie ekspansja shella
_PULL_JQ='
foreach inputs as $e ({l: {}};
  if ($e.error // null) != null then .err = $e.error
  else
    ($e.id // "") as $id
    | if $id != "" and $e.status == "Downloading" and ($e.progressDetail.total // 0) > 0
      then .l[$id] = {c: $e.progressDetail.current, t: $e.progressDetail.total}
      elif $id != "" and (.l[$id].t // 0) > 0
        and ($e.status | IN("Download complete", "Verifying Checksum", "Extracting", "Pull complete", "Already exists"))
      then .l[$id].c = .l[$id].t
      else . end
  end;
  if .err then "ERR \(.err)"
  else ([.l[].t] | add // 0) as $t
    | if $t > 0 then "P \(([.l[].c] | add) / $t)" else "P 0" end
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
      emit pull "$(_pull_ratio "$i" "$n" "$maxf")" "Pobieranie obrazu $img"
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
    emit pull "$(_pull_ratio "$i" "$n" 0)" "Pobieranie obrazu $img"
    if [ "$use_socket" = 1 ]; then
      _pull_image_socket "$img" "$i" "$n" || return 1
    else
      docker pull "$img" >&2 || return 1
    fi
    i=$((i + 1))
    emit pull "$(_pull_ratio "$i" "$n" 0)" "Pobrano obraz $img"
  done
}
