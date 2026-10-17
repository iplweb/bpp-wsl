#!/usr/bin/env bats
# Wymaga Dockera (socket hosta). Pracuje na kopii BPP_HOME w distro/tests/.tmp
# (sciezka widoczna dla demona hosta), ze stubami state/.

setup() {
  source "$BPP_HOME/lib/cert.sh"
  TMP_ROOT="$(dirname "$BATS_TEST_DIRNAME")/tests/.tmp"
  mkdir -p "$TMP_ROOT"
  WORK="$(mktemp -d "$TMP_ROOT/compose.XXXXXX")"
  cp -R "$BPP_HOME/docker-compose.yml" "$BPP_HOME/nginx" "$BPP_HOME/dbserver" "$WORK/"
  mkdir -p "$WORK/state/ssl"
  printf 'BPP_IMAGE_TAG=x\nDJANGO_BPP_DB_NAME=bpp\nDJANGO_BPP_DB_USER=bpp\nDJANGO_BPP_DB_PASSWORD=pw\n' > "$WORK/state/.env"
  printf 'DJANGO_BPP_HOSTNAME=localhost\n' > "$WORK/state/hosts.env"
  cert_generate "localhost,127.0.0.1" "$WORK/state/ssl"
  FIXTURE_SSL="$WORK/state/ssl"
}

teardown() { rm -rf "$WORK"; }

@test "compose config is valid with 7 services and only 8443 published" {
  run bash -c "cd $WORK && docker compose --env-file state/.env config --format json"
  [ "$status" -eq 0 ]
  [ "$(echo "$output" | jq '.services|keys|length')" = 7 ]
  [ "$(echo "$output" | jq -r '[.services[].ports[]?.published]|join(",")')" = 8443 ]
  [ "$(echo "$output" | jq -r '[.services[].restart]|unique|join(",")')" = unless-stopped ]
}

@test "compose project name and volumes" {
  run bash -c "cd $WORK && docker compose --env-file state/.env config --format json"
  [ "$status" -eq 0 ]
  [ "$(echo "$output" | jq -r .name)" = bpp ]
  [ "$(echo "$output" | jq -r '.volumes|keys|join(",")')" = media,postgresql_data,redis,staticfiles ]
}

@test "nginx config parses" {
  run docker run --rm -v "$WORK/nginx/nginx.conf:/etc/nginx/nginx.conf:ro" -v "$FIXTURE_SSL:/etc/nginx/ssl:ro" --add-host appserver:127.0.0.1 nginx:1.28 nginx -t
  [ "$status" -eq 0 ]
}

@test "no HSTS anywhere" {
  run grep -ri strict-transport "$BPP_HOME/nginx/nginx.conf"
  [[ "$output" == *proxy_hide_header* ]]
  [[ "$output" != *add_header* ]]
}
