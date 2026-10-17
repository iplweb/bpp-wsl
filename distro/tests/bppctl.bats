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
