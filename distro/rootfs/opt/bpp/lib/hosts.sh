# shellcheck shell=bash
# Obsluga listy hostow (CSV): localhost i adresy IPv4.

BPP_HTTPS_PORT=8443

_hosts_is_ipv4() {
  local ip="$1" octet
  local -a parts
  [[ "$ip" =~ ^[0-9]{1,3}(\.[0-9]{1,3}){3}$ ]] || return 1
  IFS=. read -ra parts <<<"$ip"
  for octet in "${parts[@]}"; do
    ((10#$octet <= 255)) || return 1
  done
}

# hosts_validate <csv> -> 0 gdy kazdy element to localhost albo IPv4.
hosts_validate() {
  local csv="$1" h
  local -a items
  [ -n "$csv" ] || return 1
  IFS=, read -ra items <<<"$csv"
  for h in "${items[@]}"; do
    [ "$h" = localhost ] || _hosts_is_ipv4 "$h" || return 1
  done
}

# hosts_render_env <csv> -> zawartosc hosts.env na stdout.
hosts_render_env() {
  local csv="$1" h origins=""
  local -a items
  hosts_validate "$csv" || return 1
  IFS=, read -ra items <<<"$csv"
  for h in "${items[@]}"; do
    origins+="${origins:+,}https://${h}:${BPP_HTTPS_PORT}"
  done
  if [ "$csv" = localhost ]; then
    echo "DJANGO_BPP_HOSTNAME=localhost"
  else
    echo "DJANGO_BPP_HOSTNAMES=$csv"
  fi
  echo "DJANGO_BPP_CSRF_EXTRA_ORIGINS=$origins"
}
