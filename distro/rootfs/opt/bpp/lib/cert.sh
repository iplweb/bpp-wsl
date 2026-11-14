# shellcheck shell=bash
# Certyfikat self-signed (RSA 2048) z SAN dla listy hostow.
# Wymogi Apple (macOS/iOS) wobec certyfikatow serwera TLS: waznosc <= 825 dni i EKU serverAuth;
# bez tego Safari i inne przegladarki na urzadzeniach Apple w LAN moga odrzucic certyfikat bez
# mozliwosci pominiecia ostrzezenia. Dlatego 825 dni + odnawianie (cert_needs_renewal).

BPP_CERT_DAYS=825
BPP_CERT_RENEW_BEFORE_DAYS=30

# cert_needs_renewal <dir> -> 0 gdy <dir>/cert.pem wygasa w ciagu BPP_CERT_RENEW_BEFORE_DAYS dni
# (albo juz wygasl / nie daje sie odczytac).
cert_needs_renewal() {
  ! openssl x509 -in "$1/cert.pem" -noout -checkend "$((BPP_CERT_RENEW_BEFORE_DAYS * 86400))" >/dev/null 2>&1
}

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
  if ! openssl req -x509 -newkey rsa:2048 -nodes -days "$BPP_CERT_DAYS" -subj /CN=localhost \
    -addext "subjectAltName=$san" -addext "extendedKeyUsage=serverAuth" -keyout "$tmp/key.pem" -out "$tmp/cert.pem" 2>/dev/null; then
    rm -rf "$tmp"
    return 1
  fi
  chmod 0644 "$tmp/key.pem" "$tmp/cert.pem"
  mv -f "$tmp/key.pem" "$dir/key.pem" && mv -f "$tmp/cert.pem" "$dir/cert.pem"
  local rc=$?
  rm -rf "$tmp"
  return $rc
}
