#!/usr/bin/env bash
# E2E stosu BPP na macOS (Docker/OrbStack).
#
#   distro/tests/e2e.sh [tag]      (albo BPP_E2E_IMAGE_TAG=<tag>)
#
# bpp-ctl dziala w kontenerze testowym (obraz z build/test-distro.sh: Linux,
# coreutils `timeout`, curl --unix-socket), z socketem Dockera hosta i repo pod
# ta sama sciezka. BPP_HOME to kopia distro/rootfs/opt/bpp w distro/tests/.tmp/.
# Asercje curl ida z hosta na https://localhost:8443 i https://<LAN>:8443.
# Na koniec (i przy bledzie) `docker compose down -v` projektu `bpp`.
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
image="bpp-distro-tests"
tag="${1:-${BPP_E2E_IMAGE_TAG:-202609.1402}}"
tmp="$repo/distro/tests/.tmp/e2e"
BPP_HOME="$tmp/bpp"
jar="$tmp/cookies.txt"
failures=0

log() { printf '[e2e] %s\n' "$*" >&2; }
pass() { log "PASS: $*"; }
fail() {
  log "FAIL: $*"
  failures=$((failures + 1))
}
die() {
  log "BLAD: $*"
  exit 1
}

LAN="$(ipconfig getifaddr en0 2>/dev/null || true)"
[ -n "$LAN" ] || LAN="$(ipconfig getifaddr en1 2>/dev/null || true)"
[ -n "$LAN" ] || die "Brak adresu LAN (ipconfig getifaddr en0/en1 puste) - podlacz siec"
HOSTS="localhost,$LAN"

compose() {
  docker compose --project-directory "$BPP_HOME" -f "$BPP_HOME/docker-compose.yml" \
    --env-file "$BPP_HOME/state/.env" "$@"
}

cleanup() {
  if [ -f "$BPP_HOME/state/.env" ]; then
    log "Sprzatanie: docker compose down -v (projekt bpp)"
    compose down -v --remove-orphans >&2 || true
  fi
}
trap cleanup EXIT

# ctl <args...> - bpp-ctl w kontenerze testowym; stdout = linie JSON.
ctl() {
  docker run --rm \
    -v "$repo:$repo" \
    -v /var/run/docker.sock:/var/run/docker.sock \
    -e BPP_HOME="$BPP_HOME" \
    -e BPP_PULL_EMIT_INTERVAL=15 \
    -e BPP_HEALTH_TIMEOUT="${BPP_E2E_HEALTH_TIMEOUT:-5400}" \
    "$image" "$BPP_HOME/bin/bpp-ctl" "$@"
}

# fresh_home - czysta kopia /opt/bpp z plikiem image-tag.
fresh_home() {
  rm -rf "$BPP_HOME"
  mkdir -p "$tmp"
  cp -R "$repo/distro/rootfs/opt/bpp" "$BPP_HOME"
  rm -rf "$BPP_HOME/state"
  printf '%s\n' "$tag" >"$BPP_HOME/image-tag"
}

# run_init - bpp-ctl init; asercja: ostatnie zdarzenie to "ready".
run_init() {
  local out last rc=0
  out="$(ctl init --hosts "$HOSTS")" || rc=$?
  printf '%s\n' "$out" >&2
  last="$(printf '%s\n' "$out" | tail -n 1 | jq -r '.step' 2>/dev/null || true)"
  if [ "$rc" -eq 0 ] && [ "$last" = ready ]; then
    pass "init --hosts $HOSTS -> ready"
  else
    fail "init --hosts $HOSTS -> rc=$rc, ostatnie zdarzenie '$last'"
    compose ps -a >&2 || true
    compose logs --tail 200 --no-color >&2 || true
    die "stos nie wstal"
  fi
}

# check_wizard <base_url> - redirect na krok admin_user i POST formularza.
check_wizard() {
  local base="$1" redirect token code location next
  local name="${base#https://}"
  local get_body="$tmp/admin_user-get-${name%%:*}.html" post_body="$tmp/admin_user-post-${name%%:*}.html"
  rm -f "$jar"

  redirect="$(curl -sk -o /dev/null -w '%{redirect_url}' "$base/" || true)"
  if [[ "$redirect" == *"/setup/step/admin_user/"* ]] &&
    { [[ "$redirect" == "$base/"* ]] || [[ "$redirect" == /* ]]; }; then
    pass "$base/ -> $redirect"
  else
    fail "$base/ -> redirect '$redirect' (oczekiwano /setup/step/admin_user/ z :8443 albo wzglednego)"
  fi

  curl -sk -c "$jar" -b "$jar" -o "$get_body" "$base/setup/step/admin_user/" || true
  # HTML jest minifikowany: atrybuty bywaja bez cudzyslowow.
  token="$(sed -En 's/.*name="?csrfmiddlewaretoken"?[^>]* value="?([^" >]+).*/\1/p' "$get_body" 2>/dev/null | head -n 1 || true)"
  [ -n "$token" ] || {
    fail "$base: brak csrfmiddlewaretoken w formularzu admin_user"
    return 0
  }

  # Kody: 200 = blad walidacji formularza, 403 = CSRF, 400 = ALLOWED_HOSTS.
  read -r code location < <(curl -sk -c "$jar" -b "$jar" -o "$post_body" \
    -w '%{http_code} %{redirect_url}\n' \
    -H "Origin: $base" -H "Referer: $base/setup/step/admin_user/" \
    --data-urlencode "csrfmiddlewaretoken=$token" \
    --data-urlencode "username=admin" \
    --data-urlencode "email=admin@example.com" \
    --data-urlencode "password1=E2e-Haslo-7319!xq" \
    --data-urlencode "password2=E2e-Haslo-7319!xq" \
    "$base/setup/step/admin_user/" || echo "000")
  # Kreator (django-first-run-wizard): POST tworzy admina, loguje go i przekierowuje
  # na /; middleware kieruje wtedy zalogowanego admina na /setup/step/uczelnia/.
  if [ "$code" != 302 ]; then
    fail "$base: POST admin_user -> $code '${location:-}' (oczekiwano 302; 403 = CSRF), odpowiedz: $post_body"
    return 0
  fi
  case "$location" in
    /*) location="$base$location" ;;
  esac
  next="$(curl -sk -c "$jar" -b "$jar" -o /dev/null -w '%{redirect_url}' "$location" || true)"
  if [[ "$next" == *"/setup/step/uczelnia/"* ]]; then
    pass "$base: POST admin_user -> 302 $location -> $next"
  else
    fail "$base: po POST admin_user ($location) redirect '$next' (oczekiwano /setup/step/uczelnia/)"
  fi
}

check_no_hsts() {
  local headers
  headers="$(curl -skI https://localhost:8443/ || true)"
  if grep -qi '^strict-transport-security' <<<"$headers"; then
    fail "naglowek Strict-Transport-Security obecny"
  else
    pass "brak naglowka Strict-Transport-Security"
  fi
}

check_start_idempotent() {
  local before after out rc=0
  before="$(compose ps -q | sort)"
  out="$(ctl start --hosts "$HOSTS")" || rc=$?
  printf '%s\n' "$out" >&2
  after="$(compose ps -q | sort)"
  if [ "$rc" -eq 0 ] && [ -n "$before" ] && [ "$before" = "$after" ]; then
    pass "start --hosts $HOSTS nie odtworzyl kontenerow"
  else
    fail "start --hosts $HOSTS: rc=$rc, ID kontenerow przed/po rozne"
    diff <(printf '%s\n' "$before") <(printf '%s\n' "$after") >&2 || true
  fi
}

# check_worker_healthy - workerserver (stala concurrency, luzniejszy healthcheck) dochodzi do healthy.
check_worker_healthy() {
  local h="" i
  for i in $(seq 1 60); do
    h="$(compose ps --format json workerserver | jq -rs 'flatten | .[0].Health // ""')"
    [ "$h" = healthy ] && break
    [ "$h" = unhealthy ] && break
    sleep 10
  done
  if [ "$h" = healthy ]; then pass "workerserver healthy"; else fail "workerserver: '$h'"; fi
}

# check_diag - bpp-ctl diag na prawdziwym stosie: sekcje, historia healthcheckow, brak hasla bazy.
check_diag() {
  local out pw
  out="$(ctl diag 2>&1)" || {
    fail "diag zakonczyl sie bledem"
    return
  }
  printf '%s\n' "$out" >"$tmp/diag.txt"
  pw="$(sed -n 's/^DJANGO_BPP_DB_PASSWORD=//p' "$BPP_HOME/state/.env")"
  if [[ "$out" == *"===== Healthcheck kontenerów ====="* ]] && [[ "$out" == *"bpp-workerserver-1: state=running"* ]] &&
    [[ "$out" == *"===== Logi usług"* ]] && [[ "$out" != *"$pw"* ]]; then
    pass "diag ($(wc -l <"$tmp/diag.txt") linii, $tmp/diag.txt)"
  else
    fail "diag: brak sekcji albo widoczne haslo ($tmp/diag.txt)"
  fi
}

# check_retry_skips_pull - ponowny init nie pobiera obrazow, ktore juz sa.
check_retry_skips_pull() {
  local out n
  out="$(ctl init --hosts "$HOSTS")"
  n="$(printf '%s\n' "$out" | jq -s '[.[] | select(.step == "pull" and (.message | test("jest już pobrany")))] | length')"
  if [ "$n" -ge 7 ] && [ "$(printf '%s\n' "$out" | tail -n 1 | jq -r .step)" = ready ]; then
    pass "ponowny init bez pobierania ($n obrazow juz pobranych)"
  else
    fail "ponowny init: $n obrazow pominietych"
  fi
}

main() {
  log "tag obrazow: $tag, hosty: $HOSTS, BPP_HOME: $BPP_HOME"
  docker build -q -t "$image" "$repo/distro/tests" >/dev/null

  # Pozostalosci po przerwanym przebiegu.
  if [ -f "$BPP_HOME/state/.env" ]; then compose down -v --remove-orphans >&2 || true; fi

  fresh_home
  run_init
  check_wizard "https://localhost:8443"
  check_no_hsts
  check_start_idempotent
  check_worker_healthy
  check_diag
  check_retry_skips_pull

  log "Swieza baza: down -v i ponowny init"
  compose down -v --remove-orphans >&2
  fresh_home
  run_init
  check_wizard "https://$LAN:8443"

  if [ "$failures" -eq 0 ]; then
    log "WSZYSTKIE ASERCJE PASS"
  else
    compose logs --tail 300 --no-color appserver webserver >&2 || true
    die "$failures asercji FAIL"
  fi
}

main "$@"
