# shellcheck shell=bash
# Lokalne CA + certyfikat serwera (RSA 2048) z SAN dla listy hostow.
#
# CA powstaje raz (cert_ca_ensure) i to jemu ufa Windows: aplikacja BPP dodaje ca.pem do magazynu
# "Zaufane glowne urzedy certyfikacji" biezacego uzytkownika. Certyfikat serwera jest podpisany przez CA,
# wiec moze byc wymieniany (zmiana listy hostow, odnowienie) bez ponownego importu w Windows.
#
# Klucz CA pozwala wystawic certyfikat zaufany na tym koncie Windows, dlatego:
#  - ca.key ma tryb 0600 i lezy poza katalogiem montowanym do kontenera nginx,
#  - CA ma krytyczne nameConstraints: z nazw DNS tylko localhost (adresy IPv4 dowolne - lista hostow
#    to adresy LAN, ktore sie zmieniaja), wiec nie da sie nim podpisac certyfikatu dla prawdziwej domeny.
#
# Wymogi Apple (macOS/iOS) wobec certyfikatow serwera TLS: waznosc <= 825 dni i EKU serverAuth;
# bez tego Safari i inne przegladarki na urzadzeniach Apple w LAN moga odrzucic certyfikat bez
# mozliwosci pominiecia ostrzezenia. Dlatego 825 dni + odnawianie (cert_needs_renewal).
# Limit dotyczy certyfikatu serwera, nie CA.

BPP_CERT_DAYS=825
BPP_CERT_RENEW_BEFORE_DAYS=30
BPP_CA_DAYS=3650
# Po tej nazwie aplikacja Windows rozpoznaje certyfikaty CA BPP w magazynie (LocalCa.cs).
BPP_CA_SUBJECT="/O=BPP/CN=BPP localhost CA"

# cert_needs_renewal <dir> -> 0 gdy <dir>/cert.pem wygasa w ciagu BPP_CERT_RENEW_BEFORE_DAYS dni
# (albo juz wygasl / nie daje sie odczytac).
cert_needs_renewal() {
  ! openssl x509 -in "$1/cert.pem" -noout -checkend "$((BPP_CERT_RENEW_BEFORE_DAYS * 86400))" >/dev/null 2>&1
}

# cert_signed_by_ca <dir> <ca_dir> -> 0 gdy <dir>/cert.pem jest podpisany przez <ca_dir>/ca.pem
# (nie: certyfikat self-signed ze starszej wersji albo CA zostalo wymienione).
cert_signed_by_ca() {
  [ -f "$2/ca.pem" ] && [ -f "$1/cert.pem" ] &&
    openssl verify -no_check_time -CAfile "$2/ca.pem" "$1/cert.pem" >/dev/null 2>&1
}

_CERT_LIB_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source-path=SCRIPTDIR source=hosts.sh
source "$_CERT_LIB_DIR/hosts.sh"

# cert_ca_ensure <ca_dir> -> <ca_dir>/ca.pem (0644) i <ca_dir>/ca.key (0600).
# Nic nie robi, gdy para istnieje, pasuje do siebie i CA nie wygasa w ciagu BPP_CERT_RENEW_BEFORE_DAYS dni.
cert_ca_ensure() {
  local dir="$1" tmp rc
  if [ -f "$dir/ca.key" ] &&
    openssl x509 -in "$dir/ca.pem" -noout -checkend "$((BPP_CERT_RENEW_BEFORE_DAYS * 86400))" >/dev/null 2>&1 &&
    [ "$(openssl x509 -in "$dir/ca.pem" -noout -pubkey 2>/dev/null)" = "$(openssl pkey -in "$dir/ca.key" -pubout 2>/dev/null)" ]; then
    return 0
  fi
  mkdir -p "$dir" || return 1
  tmp="$(mktemp -d)" || return 1
  cat >"$tmp/ca.cnf" <<'EOF'
[req]
distinguished_name = dn
x509_extensions = ext
prompt = no
[dn]
[ext]
basicConstraints = critical, CA:TRUE, pathlen:0
keyUsage = critical, keyCertSign, cRLSign
extendedKeyUsage = serverAuth
subjectKeyIdentifier = hash
nameConstraints = critical, permitted;DNS:localhost, permitted;IP:0.0.0.0/0.0.0.0
EOF
  if ! (umask 077 && openssl req -x509 -newkey rsa:2048 -nodes -days "$BPP_CA_DAYS" -config "$tmp/ca.cnf" \
    -subj "$BPP_CA_SUBJECT" -keyout "$tmp/ca.key" -out "$tmp/ca.pem" 2>/dev/null); then
    rm -rf "$tmp"
    return 1
  fi
  chmod 0600 "$tmp/ca.key" && chmod 0644 "$tmp/ca.pem" &&
    mv -f "$tmp/ca.key" "$dir/ca.key" && mv -f "$tmp/ca.pem" "$dir/ca.pem"
  rc=$?
  rm -rf "$tmp"
  return $rc
}

# cert_generate <csv> <dir> <ca_dir> -> <dir>/cert.pem i <dir>/key.pem (oba 0644), podpisane przez CA
# z <ca_dir> (tworzone w razie potrzeby). Klucz serwera jest czytelny dla nginx w kontenerze
# (instancja testowa); klucz CA nie.
cert_generate() {
  local csv="$1" dir="$2" ca_dir="${3:-}" h san="DNS:localhost,IP:127.0.0.1" tmp rc
  local -a items
  hosts_validate "$csv" || return 1
  [ -n "$ca_dir" ] || return 1
  cert_ca_ensure "$ca_dir" || return 1
  mkdir -p "$dir" || return 1
  IFS=, read -ra items <<<"$csv"
  for h in "${items[@]}"; do
    case "$h" in
      localhost | 127.0.0.1) ;; # juz w SAN
      *) san+=",IP:$h" ;;
    esac
  done
  tmp="$(mktemp -d)" || return 1
  cat >"$tmp/leaf.cnf" <<EOF
[req]
distinguished_name = dn
x509_extensions = ext
prompt = no
[dn]
CN = localhost
[ext]
basicConstraints = critical, CA:FALSE
keyUsage = critical, digitalSignature, keyEncipherment
extendedKeyUsage = serverAuth
subjectKeyIdentifier = hash
authorityKeyIdentifier = keyid
subjectAltName = $san
EOF
  if ! openssl req -x509 -CA "$ca_dir/ca.pem" -CAkey "$ca_dir/ca.key" -newkey rsa:2048 -nodes \
    -days "$BPP_CERT_DAYS" -config "$tmp/leaf.cnf" -keyout "$tmp/key.pem" -out "$tmp/cert.pem" 2>/dev/null; then
    rm -rf "$tmp"
    return 1
  fi
  chmod 0644 "$tmp/key.pem" "$tmp/cert.pem"
  mv -f "$tmp/key.pem" "$dir/key.pem" && mv -f "$tmp/cert.pem" "$dir/cert.pem"
  rc=$?
  rm -rf "$tmp"
  return $rc
}
