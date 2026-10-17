# shellcheck shell=bash
# Stan instalacji BPP: state/.env, state/hosts oraz wywolanie docker compose.

BPP_HOME="${BPP_HOME:-/opt/bpp}"

# bpp_compose <args...> - jedyne miejsce wywolujace docker compose (ruling R1:
# BPP_IMAGE_TAG jest interpolowany z state/.env).
bpp_compose() {
  docker compose --project-directory "$BPP_HOME" -f "$BPP_HOME/docker-compose.yml" \
    --env-file "$BPP_HOME/state/.env" "$@"
}

# state_ensure_env - tworzy state/.env (0600) tylko gdy nie istnieje, wiec
# haslo bazy i SECRET_KEY przetrwaja kolejne wywolania init.
state_ensure_env() {
  local state="$BPP_HOME/state" tag tmp
  [ -f "$state/.env" ] && return 0
  [ -r "$BPP_HOME/image-tag" ] || {
    echo "brak pliku $BPP_HOME/image-tag" >&2
    return 1
  }
  tag="$(tr -d '[:space:]' <"$BPP_HOME/image-tag")"
  [ -n "$tag" ] || {
    echo "pusty plik $BPP_HOME/image-tag" >&2
    return 1
  }
  mkdir -p "$state" || return 1
  tmp="$(umask 077 && mktemp "$state/.env.XXXXXX")" || return 1
  {
    echo "DJANGO_BPP_DB_NAME=bpp"
    echo "DJANGO_BPP_DB_USER=bpp"
    echo "DJANGO_BPP_DB_PASSWORD=$(openssl rand -hex 24)"
    echo "DJANGO_BPP_DB_HOST=dbserver"
    echo "DJANGO_BPP_DB_PORT=5432"
    echo "DJANGO_BPP_REDIS_HOST=redis"
    echo "DJANGO_BPP_REDIS_PORT=6379"
    echo "DJANGO_BPP_REDIS_DB_BROKER=1"
    echo "DJANGO_BPP_REDIS_DB_CELERY=2"
    echo "DJANGO_BPP_SECRET_KEY=$(openssl rand -hex 50)"
    echo "ALTCHA_HMAC_KEY=$(openssl rand -hex 32)"
    echo "STATIC_ROOT=/staticroot/"
    echo "DJANGO_BPP_MEDIA_ROOT=/mediaroot"
    echo "DJANGO_SETTINGS_MODULE=django_bpp.settings.production"
    echo "DJANGO_BPP_SSL_MODE=manual"
    echo "DJANGO_BPP_ADMIN_USERNAME=admin"
    echo "DJANGO_BPP_ADMIN_EMAIL=admin@localhost"
    echo "BPP_IMAGE_TAG=$tag"
  } >"$tmp" || {
    rm -f "$tmp"
    return 1
  }
  chmod 600 "$tmp" && mv -f "$tmp" "$state/.env"
}

# state_hosts_changed <csv> -> 0 gdy rozni sie od state/hosts (brak pliku = zmiana).
state_hosts_changed() {
  local csv="$1" cur
  [ -f "$BPP_HOME/state/hosts" ] || return 0
  cur="$(cat "$BPP_HOME/state/hosts")"
  [ "$cur" != "$csv" ]
}
