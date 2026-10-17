# shellcheck shell=bash
# Certyfikat self-signed (RSA 2048, 3650 dni) z SAN dla listy hostow.

_CERT_LIB_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source-path=SCRIPTDIR source=hosts.sh
source "$_CERT_LIB_DIR/hosts.sh"

# cert_generate <csv> <dir> -> <dir>/cert.pem i <dir>/key.pem (oba 0644).
# Klucz jest czytelny dla nginx w kontenerze (instancja testowa).
cert_generate() {
  local csv="$1" dir="$2" h san="DNS:localhost,IP:127.0.0.1" tmp
  local -a items
  hosts_validate "$csv" || return 1
  mkdir -p "$dir" || return 1
  IFS=, read -ra items <<<"$csv"
  for h in "${items[@]}"; do
    case "$h" in
      localhost | 127.0.0.1) ;; # juz w SAN
      *) san+=",IP:$h" ;;
    esac
  done
  tmp="$(mktemp -d)" || return 1
  if ! openssl req -x509 -newkey rsa:2048 -nodes -days 3650 -subj /CN=localhost \
    -addext "subjectAltName=$san" -keyout "$tmp/key.pem" -out "$tmp/cert.pem" 2>/dev/null; then
    rm -rf "$tmp"
    return 1
  fi
  chmod 0644 "$tmp/key.pem" "$tmp/cert.pem"
  mv -f "$tmp/key.pem" "$dir/key.pem" && mv -f "$tmp/cert.pem" "$dir/cert.pem"
  local rc=$?
  rm -rf "$tmp"
  return $rc
}
