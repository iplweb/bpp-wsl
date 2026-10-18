# shellcheck shell=bash
# emit <step> <progress|null> <message> [item] -> jedna linia JSON na stdout.
# Kroki: config, cert, pull, start, migrate, ready, error.
# item (opcjonalny, 0..1): postep biezacego elementu (obrazu w kroku pull); bez niego pola "item" nie ma.
emit() {
  local step="$1" progress="$2" message="$3" item="${4:-null}"
  jq -cn --arg step "$step" --arg message "$message" --argjson progress "$progress" --argjson item "$item" \
    '{step: $step, progress: $progress, message: $message} + (if $item == null then {} else {item: $item} end)'
}
