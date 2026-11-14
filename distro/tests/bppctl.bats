#!/usr/bin/env bats
# bpp-ctl z atrapa `docker` w PATH (tests/mockbin). Praca na kopii BPP_HOME w tests/.tmp.

setup() {
  TMP_ROOT="$(dirname "$BATS_TEST_DIRNAME")/tests/.tmp"
  mkdir -p "$TMP_ROOT"
  WORK="$(mktemp -d "$TMP_ROOT/bppctl.XXXXXX")"
  cp -R "$BPP_HOME/bin" "$BPP_HOME/lib" "$BPP_HOME/docker-compose.yml" "$WORK/"
  echo t1 >"$WORK/image-tag"
  export BPP_HOME="$WORK"
  export MOCK_DOCKER_LOG="$WORK/docker.log"
  : >"$MOCK_DOCKER_LOG"
  export BPP_DOCKER_SOCK="$WORK/no-such.sock" # wymusza sciezke `docker pull`
  export BPP_HEALTH_TIMEOUT=3 BPP_HEALTH_INTERVAL=1
  export PATH="$BATS_TEST_DIRNAME/mockbin:$PATH"
  export CTL="$WORK/bin/bpp-ctl"
}

teardown() { rm -rf "$WORK"; }

last_step() { echo "${lines[-1]}" | jq -r .step; }

@test "init twice keeps DB password" {
  run bash -c '"$CTL" init --hosts localhost 2>/dev/null'
  [ "$status" -eq 0 ]
  p1=$(grep DB_PASSWORD "$WORK/state/.env")
  run bash -c '"$CTL" init --hosts localhost 2>/dev/null'
  [ "$status" -eq 0 ]
  [ "$p1" = "$(grep DB_PASSWORD "$WORK/state/.env")" ]
}

@test "state/.env has required vars and mode 600" {
  run bash -c '"$CTL" init --hosts localhost 2>/dev/null'
  [ "$status" -eq 0 ]
  for v in DJANGO_BPP_DB_NAME=bpp DJANGO_BPP_DB_USER=bpp DJANGO_BPP_DB_HOST=dbserver DJANGO_BPP_DB_PORT=5432 \
    DJANGO_BPP_REDIS_HOST=redis DJANGO_BPP_REDIS_PORT=6379 DJANGO_BPP_REDIS_DB_BROKER=1 DJANGO_BPP_REDIS_DB_CELERY=2 \
    STATIC_ROOT=/staticroot/ DJANGO_BPP_MEDIA_ROOT=/mediaroot DJANGO_SETTINGS_MODULE=django_bpp.settings.production \
    DJANGO_BPP_SSL_MODE=manual DJANGO_BPP_ADMIN_USERNAME=admin DJANGO_BPP_ADMIN_EMAIL=admin@localhost BPP_IMAGE_TAG=t1; do
    grep -qx "$v" "$WORK/state/.env"
  done
  [ "$(grep -c '^DJANGO_BPP_SECRET_KEY=.\{100\}$' "$WORK/state/.env")" = 1 ]
  [ "$(grep -c '^DJANGO_BPP_DB_PASSWORD=.\{48\}$' "$WORK/state/.env")" = 1 ]
  [ "$(stat -c %a "$WORK/state/.env")" = 600 ]
}

@test "init without image-tag -> error event, exit 1" {
  rm "$WORK/image-tag"
  run bash -c '"$CTL" init --hosts localhost 2>/dev/null'
  [ "$status" -eq 1 ]
  [ "$(last_step)" = error ]
  [ ! -e "$WORK/state/.env" ]
}

@test "start with same hosts does not regenerate cert" {
  "$CTL" init --hosts localhost >/dev/null 2>&1
  m1=$(md5sum "$WORK/state/ssl/cert.pem")
  run bash -c '"$CTL" start --hosts localhost 2>/dev/null'
  [ "$status" -eq 0 ]
  [ "$m1" = "$(md5sum "$WORK/state/ssl/cert.pem")" ]
}

@test "start regenerates a cert that is about to expire" {
  run bash -c '"$CTL" init --hosts localhost 2>/dev/null'
  [ "$status" -eq 0 ]
  # podmiana na certyfikat wazny 10 dni (< 30 dni progu odnowienia)
  bash -c 'source "$BPP_HOME/lib/cert.sh"; BPP_CERT_DAYS=10 cert_generate localhost "$BPP_HOME/state/ssl" "$BPP_HOME/state/ca"'
  m1=$(md5sum "$WORK/state/ssl/cert.pem")
  run bash -c '"$CTL" start --hosts localhost 2>/dev/null'
  [ "$status" -eq 0 ]
  [ "$m1" != "$(md5sum "$WORK/state/ssl/cert.pem")" ]
  openssl x509 -in "$WORK/state/ssl/cert.pem" -noout -checkend $((800 * 86400))
}

@test "ca-cert prints the CA the server cert chains to; hosts change keeps the CA" {
  run bash -c '"$CTL" ca-cert 2>/dev/null'
  [ "$status" -eq 1 ]
  [ "$(last_step)" = error ]
  "$CTL" init --hosts localhost >/dev/null 2>&1
  run bash -c '"$CTL" ca-cert 2>/dev/null'
  [ "$status" -eq 0 ]
  [ "$output" = "$(cat "$WORK/state/ca/ca.pem")" ]
  ca1="$output"
  "$CTL" start --hosts localhost,10.1.1.1 >/dev/null 2>&1
  [ "$ca1" = "$("$CTL" ca-cert 2>/dev/null)" ]
  openssl verify -CAfile "$WORK/state/ca/ca.pem" "$WORK/state/ssl/cert.pem"
}

@test "start replaces a self-signed cert left by a version without the local CA" {
  "$CTL" init --hosts localhost >/dev/null 2>&1
  rm -r "$WORK/state/ca"
  openssl req -x509 -newkey rsa:2048 -nodes -days 800 -subj /CN=localhost \
    -keyout "$WORK/state/ssl/key.pem" -out "$WORK/state/ssl/cert.pem" 2>/dev/null
  run bash -c '"$CTL" start --hosts localhost 2>/dev/null'
  [ "$status" -eq 0 ]
  openssl verify -CAfile "$WORK/state/ca/ca.pem" "$WORK/state/ssl/cert.pem"
}

@test "start with new hosts regenerates cert and hosts.env" {
  "$CTL" init --hosts localhost >/dev/null 2>&1
  m1=$(md5sum "$WORK/state/ssl/cert.pem")
  run bash -c '"$CTL" start --hosts localhost,10.1.1.1 2>/dev/null'
  [ "$status" -eq 0 ]
  [ "$m1" != "$(md5sum "$WORK/state/ssl/cert.pem")" ]
  grep -q 10.1.1.1 "$WORK/state/hosts.env"
  openssl x509 -in "$WORK/state/ssl/cert.pem" -noout -ext subjectAltName | grep -q 10.1.1.1
  [ "$(cat "$WORK/state/hosts")" = localhost,10.1.1.1 ]
}

@test "unchanged hosts: hosts.env and hosts untouched, no force-recreate" {
  "$CTL" init --hosts localhost >/dev/null 2>&1
  h1=$(md5sum "$WORK/state/hosts.env" "$WORK/state/hosts")
  : >"$MOCK_DOCKER_LOG"
  run bash -c '"$CTL" start --hosts localhost 2>/dev/null'
  [ "$status" -eq 0 ]
  [ "$h1" = "$(md5sum "$WORK/state/hosts.env" "$WORK/state/hosts")" ]
  ! grep -q -- '--force-recreate' "$MOCK_DOCKER_LOG"
}

@test "migrate is emitted before a blocking up, and up is bounded by the timeout" {
  "$CTL" init --hosts localhost >/dev/null 2>&1
  start=$SECONDS
  MOCK_UP_SLEEP=10 BPP_HEALTH_TIMEOUT=2 run bash -c '"$CTL" start --hosts localhost 2>/dev/null'
  [ "$status" -eq 1 ]
  [ $((SECONDS - start)) -lt 8 ]
  [ "$(echo "${lines[-2]}" | jq -r .step)" = migrate ]
  [ "$(last_step)" = error ]
  [[ "${lines[-1]}" == *limit* ]]
}

@test "failing or empty openssl rand -> error, no state/.env" {
  mkdir -p "$WORK/failbin"
  printf '#!/bin/sh\nexit 1\n' >"$WORK/failbin/openssl"
  chmod +x "$WORK/failbin/openssl"
  PATH="$WORK/failbin:$PATH" run bash -c '"$CTL" init --hosts localhost 2>/dev/null'
  [ "$status" -eq 1 ]
  [ "$(last_step)" = error ]
  [ ! -e "$WORK/state/.env" ]
  printf '#!/bin/sh\nexit 0\n' >"$WORK/failbin/openssl"
  PATH="$WORK/failbin:$PATH" run bash -c '"$CTL" init --hosts localhost 2>/dev/null'
  [ "$status" -eq 1 ]
  [ ! -e "$WORK/state/.env" ]
}

@test "init follows service logs on stderr and stops the follower; start does not" {
  run bash -c 'MOCK_WEBSERVER_RUNNING=1 MOCK_UP_SLEEP=2 "$CTL" init --hosts localhost 2>/dev/null'
  [ "$status" -eq 0 ]
  [ "$(last_step)" = ready ]
  grep -q "logs --follow" "$MOCK_DOCKER_LOG"
  : >"$MOCK_DOCKER_LOG"
  run bash -c 'MOCK_WEBSERVER_RUNNING=1 MOCK_UP_SLEEP=2 "$CTL" start --hosts localhost 2>/dev/null'
  [ "$status" -eq 0 ]
  ! grep -q "logs --follow" "$MOCK_DOCKER_LOG"
}

@test "failed init leaves no log follower behind" {
  run bash -c 'MOCK_UP_RC=1 "$CTL" init --hosts localhost 2>/dev/null'
  [ "$status" -eq 1 ]
  [ "$(last_step)" = error ]
  sleep 2
  ! pgrep -f "ps --all -q appserver" >/dev/null
}

@test "init --demo generates demo data once, with the chosen theme" {
  run bash -c '"$CTL" init --hosts localhost --demo lem 2>/dev/null'
  [ "$status" -eq 0 ]
  [ "$(last_step)" = ready ]
  [[ "$output" == *'"step":"demo"'* ]]
  # three separate commands, each announced, so the installer always says which stage is running
  [ "$(grep -c "exec -T appserver python src/manage.py" "$MOCK_DOCKER_LOG")" = 3 ]
  grep -q "manage.py seed_default_dyscypliny$" "$MOCK_DOCKER_LOG"
  grep -q "manage.py create_demo_data --motyw lem " "$MOCK_DOCKER_LOG"
  grep -q "manage.py denorm_flush$" "$MOCK_DOCKER_LOG"
  m=$(printf '%s\n' "${lines[@]}" | jq -sr '[.[]|select(.step=="demo").message|sub("^.* — "; "")]|join(";")')
  [ "$m" = "słownik dyscyplin (1 z 3);autorzy i publikacje (2 z 3, kilka minut);opisy bibliograficzne (3 z 3, kilka minut);Dane demo zainstalowane" ]
  [ "$(cat "$WORK/state/demo")" = lem ]
  : >"$MOCK_DOCKER_LOG"
  run bash -c '"$CTL" init --hosts localhost --demo lem 2>/dev/null'
  [ "$status" -eq 0 ]
  ! grep -q "exec -T appserver" "$MOCK_DOCKER_LOG"
}

@test "init without --demo generates nothing; unknown theme and start --demo are rejected" {
  run bash -c '"$CTL" init --hosts localhost 2>/dev/null'
  [ "$status" -eq 0 ]
  ! grep -q "exec -T appserver" "$MOCK_DOCKER_LOG"
  [ ! -e "$WORK/state/demo" ]
  run bash -c '"$CTL" init --hosts localhost --demo disney 2>/dev/null'
  [ "$status" -eq 1 ]
  [ "$(last_step)" = error ]
  run bash -c '"$CTL" start --hosts localhost --demo lem 2>/dev/null'
  [ "$status" -eq 1 ]
}

# _pull_fracs <linie JSON> - ulamki "P" z programu jq dla strumienia zdarzen pobierania
_pull_fracs() {
  printf '%s\n' "$@" | bash -c 'source "$BPP_HOME/lib/pull.sh"; jq -rn "$_PULL_JQ"' | awk '{ printf "%.3f\n", $2 }'
}

@test "pull progress counts announced layers, so one small finished layer is not 100%" {
  run _pull_fracs \
    '{"status":"Pulling from library/x","id":"t1"}' \
    '{"status":"Pulling fs layer","id":"a"}' \
    '{"status":"Pulling fs layer","id":"b"}' \
    '{"status":"Pulling fs layer","id":"c"}' \
    '{"status":"Pulling fs layer","id":"d"}' \
    '{"status":"Downloading","progressDetail":{"current":1000,"total":1000},"id":"a"}' \
    '{"status":"Download complete","id":"a"}' \
    '{"status":"Downloading","progressDetail":{"current":500,"total":1000},"id":"b"}'
  [ "$status" -eq 0 ]
  [ "${lines[4]}" = 0.000 ]  # tylko zapowiedzi warstw
  [ "${lines[6]}" = 0.225 ]  # 1 z 4 warstw pobrana: 0.9 * 1/4, a nie 100%
  [ "${lines[7]}" = 0.338 ]  # + polowa drugiej: 0.9 * 1.5/4
}

@test "pull progress: extraction is the last 10%, everything pulled or already present is 100%" {
  run _pull_fracs \
    '{"status":"Already exists","id":"a"}' \
    '{"status":"Pulling fs layer","id":"b"}' \
    '{"status":"Downloading","progressDetail":{"current":1000,"total":1000},"id":"b"}' \
    '{"status":"Download complete","id":"b"}' \
    '{"status":"Extracting","progressDetail":{"current":10,"total":1000},"id":"b"}' \
    '{"status":"Pull complete","id":"b"}' \
    '{"status":"Digest: sha256:abc"}'
  [ "$status" -eq 0 ]
  [ "${lines[3]}" = 0.950 ]
  [ "${lines[4]}" = 0.950 ]
  [ "${lines[5]}" = 1.000 ]
  [ "${lines[6]}" = 1.000 ]
}

@test "pull progress: error event becomes ERR" {
  run bash -c 'printf "%s\n" "{\"error\":\"boom\"}" | { source "$BPP_HOME/lib/pull.sh"; jq -rn "$_PULL_JQ"; }'
  [ "$output" = "ERR boom" ]
}

@test "socket-path progress never goes backwards" {
  source "$BPP_HOME/lib/pull.sh"
  BPP_PULL_EMIT_INTERVAL=0
  run bash -c 'source "$BPP_HOME/lib/pull.sh"; BPP_PULL_EMIT_INTERVAL=0; printf "P 0.5\nP 0.2\nP 0.7\nP 0.1\n" | _pull_consume img 0 1'
  [ "$status" -eq 0 ]
  p=$(printf '%s\n' "${lines[@]}" | jq -sr 'map(.progress*10000|round)|join(",")')
  [ "$p" = "5000,5000,7000,7000" ]
  items=$(printf '%s\n' "${lines[@]}" | jq -sr 'map(.item*10000|round)|join(",")')
  [ "$items" = "5000,5000,7000,7000" ]
  [ "$(printf '%s\n' "${lines[@]}" | jq -sr '.[0].message')" = "Pobieranie obrazu img (1 z 1)" ]
}

@test "nginx reload only when cert changed and webserver already running" {
  "$CTL" init --hosts localhost >/dev/null 2>&1
  : >"$MOCK_DOCKER_LOG"
  MOCK_WEBSERVER_RUNNING=1 "$CTL" start --hosts localhost >/dev/null 2>&1
  ! grep -q 'nginx -s reload' "$MOCK_DOCKER_LOG"
  MOCK_WEBSERVER_RUNNING=1 "$CTL" start --hosts localhost,10.1.1.1 >/dev/null 2>&1
  grep -q 'exec -T webserver nginx -s reload' "$MOCK_DOCKER_LOG"
  : >"$MOCK_DOCKER_LOG"
  MOCK_WEBSERVER_RUNNING=0 "$CTL" start --hosts localhost,10.2.2.2 >/dev/null 2>&1
  ! grep -q 'nginx -s reload' "$MOCK_DOCKER_LOG"
}

@test "every compose call uses project-directory, -f and --env-file" {
  "$CTL" init --hosts localhost >/dev/null 2>&1
  while read -r l; do
    [[ "$l" != "docker compose "* ]] ||
      [[ "$l" == "docker compose --project-directory $WORK -f $WORK/docker-compose.yml --env-file $WORK/state/.env "* ]]
  done <"$MOCK_DOCKER_LOG"
  grep -q ' up -d' "$MOCK_DOCKER_LOG"
}

@test "invalid hosts -> error event, exit 1" {
  run bash -c '"$CTL" start --hosts "x;y" 2>/dev/null'
  [ "$status" -eq 1 ]
  [ "$(last_step)" = error ]
}

@test "missing --hosts and unknown command -> error event, exit 1" {
  run bash -c '"$CTL" start 2>/dev/null'
  [ "$status" -eq 1 ]
  [ "$(last_step)" = error ]
  run bash -c '"$CTL" bogus 2>/dev/null'
  [ "$status" -eq 1 ]
  [ "$(last_step)" = error ]
}

@test "stdout is pure JSON lines" {
  run bash -c '"$CTL" init --hosts localhost 2>/dev/null'
  [ "$status" -eq 0 ]
  for l in "${lines[@]}"; do echo "$l" | jq -e . >/dev/null; done
}

@test "last event of successful init is ready" {
  run bash -c '"$CTL" init --hosts localhost 2>/dev/null'
  [ "$status" -eq 0 ]
  [ "$(last_step)" = ready ]
}

@test "pull fallback emits i/N progress per image and docker pull per image" {
  run bash -c '"$CTL" init --hosts localhost 2>/dev/null'
  [ "$status" -eq 0 ]
  [ "$(grep -c '^docker pull ' "$MOCK_DOCKER_LOG")" = 3 ]
  for img in iplweb/bpp_appserver:t1 postgres:18.4 redis:8.6.2; do grep -qx "docker pull $img" "$MOCK_DOCKER_LOG"; done
  p=$(printf '%s\n' "${lines[@]}" | jq -sr '[.[]|select(.step=="pull").progress]|map(.*10000|round|tostring)|join(",")')
  [ "$p" = "0,3333,3333,6667,6667,10000" ]
  items=$(printf '%s\n' "${lines[@]}" | jq -sr '[.[]|select(.step=="pull").item|tostring]|join(",")')
  [ "$items" = "0,1,0,1,0,1" ]
  printf '%s\n' "${lines[@]}" | jq -se 'any(.[]; .message == "Pobieranie obrazu postgres:18.4 (2 z 3)")'
}

@test "pull failure -> error event, exit 1" {
  MOCK_PULL_RC=1 run bash -c '"$CTL" init --hosts localhost 2>/dev/null'
  [ "$status" -eq 1 ]
  [ "$(last_step)" = error ]
}

@test "unhealthy appserver -> error, exit 1" {
  "$CTL" init --hosts localhost >/dev/null 2>&1
  MOCK_HEALTH=unhealthy run bash -c '"$CTL" start --hosts localhost 2>/dev/null'
  [ "$status" -eq 1 ]
  [ "$(last_step)" = error ]
}

@test "health timeout -> error, exit 1" {
  "$CTL" init --hosts localhost >/dev/null 2>&1
  MOCK_HEALTH=starting BPP_HEALTH_TIMEOUT=2 run bash -c '"$CTL" start --hosts localhost 2>/dev/null'
  [ "$status" -eq 1 ]
  [ "$(last_step)" = error ]
  [[ "${lines[-1]}" == *limit* ]]
}

@test "start without init -> error event" {
  run bash -c '"$CTL" start --hosts localhost 2>/dev/null'
  [ "$status" -eq 1 ]
  [ "$(last_step)" = error ]
}

@test "status reports one JSON line" {
  "$CTL" init --hosts localhost >/dev/null 2>&1
  run "$CTL" status
  [ "$status" -eq 0 ]
  [ "${#lines[@]}" = 1 ]
  [ "$(echo "$output" | jq -c .)" = '{"running":true,"health":"healthy"}' ]
  MOCK_HEALTH=absent run "$CTL" status
  [ "$(echo "$output" | jq -c .)" = '{"running":false,"health":"absent"}' ]
  MOCK_HEALTH=starting run "$CTL" status
  [ "$(echo "$output" | jq -c .)" = '{"running":true,"health":"starting"}' ]
}

@test "stop and logs call compose" {
  "$CTL" init --hosts localhost >/dev/null 2>&1
  run bash -c '"$CTL" stop 2>/dev/null'
  [ "$status" -eq 0 ]
  [ -z "$output" ]
  grep -q ' stop$' "$MOCK_DOCKER_LOG"
  run "$CTL" logs appserver
  [ "$status" -eq 0 ]
  [ "$output" = "mock logs --tail 500 --no-color appserver" ]
}

@test "init writes hosts.env and cert before pull (compose config needs env_file)" {
  run bash -c '"$CTL" init --hosts localhost 2>/dev/null'
  [ "$status" -eq 0 ]
  [ "$(last_step)" = ready ]
  [ -f "$WORK/state/ssl/cert.pem" ]
  [ "$(grep -c '^docker pull ' "$MOCK_DOCKER_LOG")" = 3 ]
}

@test "compose config failure -> error event, no pull of garbage" {
  MOCK_CONFIG_RC=1 run bash -c '"$CTL" init --hosts localhost 2>/dev/null'
  [ "$status" -eq 1 ]
  [ "$(last_step)" = error ]
  ! grep -q '^docker pull ' "$MOCK_DOCKER_LOG"
  for l in "${lines[@]}"; do
    [[ "$(echo "$l" | jq -r .message)" != *'"step"'* ]]
  done
}

@test "re-init with new hosts reloads running nginx" {
  "$CTL" init --hosts localhost >/dev/null 2>&1
  : >"$MOCK_DOCKER_LOG"
  MOCK_WEBSERVER_RUNNING=1 "$CTL" init --hosts localhost,10.3.3.3 >/dev/null 2>&1
  grep -q 'exec -T webserver nginx -s reload' "$MOCK_DOCKER_LOG"
}

@test "init/start/stop wait for docker info before any compose call" {
  export MOCK_DOCKER_INFO_FAILS=2 BPP_DOCKER_WAIT_INTERVAL=0.2
  run bash -c '"$CTL" init --hosts localhost 2>/dev/null'
  [ "$status" -eq 0 ]
  [ "$(last_step)" = ready ]
  [ "$(grep -c '^docker info' "$MOCK_DOCKER_LOG")" -ge 3 ]
  # pierwsze polecenie dockera to `docker info`, compose dopiero po udanym info
  [ "$(head -n 1 "$MOCK_DOCKER_LOG")" = "docker info" ]
  first_compose=$(grep -n '^docker compose' "$MOCK_DOCKER_LOG" | head -n 1 | cut -d: -f1)
  third_info=$(grep -n '^docker info' "$MOCK_DOCKER_LOG" | sed -n 3p | cut -d: -f1)
  [ "$first_compose" -gt "$third_info" ]
  printf '%s\n' "${lines[@]}" | jq -e -s 'any(.[]; .step == "config" and (.message | test("Docker")))' >/dev/null

  for cmd in "start --hosts localhost" stop; do
    rm -f "$MOCK_DOCKER_LOG.info-calls"
    : >"$MOCK_DOCKER_LOG"
    run bash -c "\"\$CTL\" $cmd 2>/dev/null"
    [ "$status" -eq 0 ]
    [ "$(head -n 1 "$MOCK_DOCKER_LOG")" = "docker info" ]
    [ "$(grep -c '^docker info' "$MOCK_DOCKER_LOG")" -ge 3 ]
  done
  # stop: stdout pusty takze podczas czekania (komunikat tylko na stderr)
  [ -z "$output" ]
}

@test "docker never starts -> error 'Docker nie wystartował', no compose calls" {
  "$CTL" init --hosts localhost >/dev/null 2>&1
  export MOCK_DOCKER_INFO_FAILS=1000 BPP_DOCKER_WAIT_TIMEOUT=1 BPP_DOCKER_WAIT_INTERVAL=0.2
  for cmd in "init --hosts localhost" "start --hosts localhost" stop; do
    rm -f "$MOCK_DOCKER_LOG.info-calls"
    : >"$MOCK_DOCKER_LOG"
    start=$SECONDS
    run bash -c "\"\$CTL\" $cmd 2>/dev/null"
    [ "$status" -eq 1 ]
    [ $((SECONDS - start)) -lt 5 ]
    [ "$(last_step)" = error ]
    [[ "$(echo "${lines[-1]}" | jq -r .message)" == *"Docker nie wystartował"* ]]
    ! grep -q '^docker compose' "$MOCK_DOCKER_LOG"
  done
}

@test "images already present are not pulled again" {
  MOCK_PRESENT_IMAGES="postgres:18.4 iplweb/bpp_appserver:t1" run bash -c '"$CTL" init --hosts localhost 2>/dev/null'
  [ "$status" -eq 0 ]
  [ "$(grep -c '^docker pull ' "$MOCK_DOCKER_LOG")" = 1 ]
  grep -qx "docker pull redis:8.6.2" "$MOCK_DOCKER_LOG"
  printf '%s\n' "${lines[@]}" | jq -se 'any(.[]; .message == "Obraz postgres:18.4 jest już pobrany (2 z 3)" and .item == 1)'
  p=$(printf '%s\n' "${lines[@]}" | jq -sr '[.[]|select(.step=="pull").progress]|map(.*10000|round|tostring)|join(",")')
  [ "$p" = "3333,6667,6667,10000" ]
}

@test "start without --hosts reuses the hosts of the last run" {
  "$CTL" init --hosts localhost,192.168.1.5 >/dev/null 2>&1
  run bash -c '"$CTL" start 2>/dev/null'
  [ "$status" -eq 0 ]
  [ "$(last_step)" = ready ]
  [ "$(cat "$WORK/state/hosts")" = "localhost,192.168.1.5" ]
}

@test "compose passes arguments through with the stack's env file" {
  "$CTL" init --hosts localhost >/dev/null 2>&1
  : >"$MOCK_DOCKER_LOG"
  run "$CTL" compose logs workerserver
  [ "$status" -eq 0 ]
  [ "$output" = "mock logs workerserver" ]
  grep -q -- "--env-file $WORK/state/.env logs workerserver" "$MOCK_DOCKER_LOG"
}

@test "diag prints sections, hides secrets and never fails" {
  "$CTL" init --hosts localhost >/dev/null 2>&1
  run "$CTL" diag
  [ "$status" -eq 0 ]
  [[ "$output" == *"===== Kontenery (compose ps -a) ====="* ]]
  [[ "$output" == *"===== Logi usług"* ]]
  [[ "$output" == *"BPP_IMAGE_TAG=t1"* ]]
  pw="$(sed -n 's/^DJANGO_BPP_DB_PASSWORD=//p' "$WORK/state/.env")"
  [ -n "$pw" ]
  [[ "$output" != *"$pw"* ]]
  MOCK_DOCKER_INFO_FAILS=1000 run timeout 120 "$CTL" diag
  [ "$status" -eq 0 ]
}

# ---- megabytes in the pull messages (image-layers.tsv written at build time) ----

# _layers_fixture - app and worker share layer a; MB = 1048576 bytes
_layers_fixture() {
  printf '%s\t%s\t%s\n' \
    app:1 sha256:a 104857600 \
    app:1 sha256:b 52428800 \
    worker:1 sha256:a 104857600 \
    worker:1 sha256:c 1048577 \
    twin:1 sha256:a 104857600 >"$WORK/image-layers.tsv"
}

@test "pull jq reports downloaded bytes: finished layers in full, existing ones as nothing" {
  run bash -c 'printf "%s\n" "$@" | { source "$BPP_HOME/lib/pull.sh"; jq -rn "$_PULL_JQ"; } | tail -1 | cut -d" " -f3' _ \
    '{"id":"a","status":"Already exists"}' \
    '{"id":"b","status":"Downloading","progressDetail":{"current":10,"total":100}}' \
    '{"id":"c","status":"Downloading","progressDetail":{"current":40,"total":50}}' \
    '{"id":"c","status":"Download complete"}'
  [ "$output" = 60 ]
}

@test "new bytes of an image: its layers minus those of images already in Docker" {
  _layers_fixture
  run bash -c 'source "$BPP_HOME/lib/pull.sh"; _pull_new_bytes app:1; echo; _pull_new_bytes worker:1 app:1; echo; _pull_new_bytes twin:1 other:9 app:1; echo; _pull_new_bytes unknown:1; echo "[end]"'
  [ "${lines[0]}" = 157286400 ]
  [ "${lines[1]}" = 1048577 ]
  [ "${lines[2]}" = 0 ]
  [ "${lines[3]}" = "[end]" ]
}

@test "new bytes are unknown without the layers file" {
  run bash -c 'source "$BPP_HOME/lib/pull.sh"; _pull_new_bytes app:1; echo "[end]"'
  [ "$output" = "[end]" ]
}

@test "pull message carries megabytes only for a known, non-zero total" {
  run bash -c 'source "$BPP_HOME/lib/pull.sh"; _pull_msg img 0 3; _pull_msg img 0 3 "" 5; _pull_msg img 0 3 0 0; _pull_msg img 1 3 1048577 1048575; _pull_msg img 2 3 314572800 10485760; _pull_msg img 2 3 1048576 9999999'
  [ "${lines[0]}" = "Pobieranie obrazu img (1 z 3)" ]
  [ "${lines[1]}" = "Pobieranie obrazu img (1 z 3)" ]
  [ "${lines[2]}" = "Pobieranie obrazu img (1 z 3)" ]
  [ "${lines[3]}" = "Pobieranie obrazu img (2 z 3), 0 MB z 2 MB" ]
  [ "${lines[4]}" = "Pobieranie obrazu img (3 z 3), 10 MB z 300 MB" ]
  [ "${lines[5]}" = "Pobieranie obrazu img (3 z 3), 1 MB z 1 MB" ]
}

@test "socket-path messages show megabytes that never go backwards" {
  run bash -c 'source "$BPP_HOME/lib/pull.sh"; BPP_PULL_EMIT_INTERVAL=0; printf "P 0.1 10485760\nP 0.2 5242880\nP 0.5 157286400\n" | _pull_consume img 0 1 314572800'
  [ "$status" -eq 0 ]
  m=$(printf '%s\n' "${lines[@]}" | jq -sr 'map(.message | sub("^.*\\), "; ""))|join(";")')
  [ "$m" = "10 MB z 300 MB;10 MB z 300 MB;150 MB z 300 MB" ]
}

@test "error text with spaces survives the socket path" {
  run bash -c 'source "$BPP_HOME/lib/pull.sh"; printf "ERR pull access denied for x\n" | _pull_consume img 0 1 2>&1'
  [ "$status" -eq 1 ]
  [ "$output" = "docker pull img: pull access denied for x" ]
}

@test "init announces each image with the megabytes it will download" {
  printf '%s\t%s\t%s\n' \
    iplweb/bpp_appserver:t1 sha256:a 209715200 \
    postgres:18.4 sha256:p 104857600 >"$WORK/image-layers.tsv"
  run bash -c '"$CTL" init --hosts localhost 2>/dev/null'
  [ "$status" -eq 0 ]
  printf '%s\n' "${lines[@]}" | jq -se 'any(.[]; .message == "Pobieranie obrazu iplweb/bpp_appserver:t1 (1 z 3), 0 MB z 200 MB")'
  printf '%s\n' "${lines[@]}" | jq -se 'any(.[]; .message == "Pobieranie obrazu postgres:18.4 (2 z 3), 0 MB z 100 MB")'
  printf '%s\n' "${lines[@]}" | jq -se 'any(.[]; .message == "Pobieranie obrazu redis:8.6.2 (3 z 3)")'
}
