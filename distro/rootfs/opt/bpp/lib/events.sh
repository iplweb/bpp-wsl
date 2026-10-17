# shellcheck shell=bash
# emit <step> <progress|null> <message> -> jedna linia JSON na stdout.
# Kroki: config, cert, pull, start, migrate, ready, error.
emit() {
  local step="$1" progress="$2" message="$3"
  jq -cn --arg step "$step" --arg message "$message" --argjson progress "$progress" \
    '{step: $step, progress: $progress, message: $message}'
}
