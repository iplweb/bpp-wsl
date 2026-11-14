@test "SAN contains localhost, 127.0.0.1 and LAN ips" {
  run bash -c 'source lib/cert.sh; d=$(mktemp -d); cert_generate localhost,192.168.1.50 $d $d/ca; openssl x509 -in $d/cert.pem -noout -ext subjectAltName'
  [[ "$output" == *"DNS:localhost"* && "$output" == *"IP Address:127.0.0.1"* && "$output" == *"IP Address:192.168.1.50"* ]]
}
@test "valid 825 days (Apple limit), not longer, and key readable" {
  run bash -c 'source lib/cert.sh; d=$(mktemp -d); cert_generate localhost $d $d/ca; openssl x509 -in $d/cert.pem -noout -checkend $((824*86400)) >/dev/null && ! openssl x509 -in $d/cert.pem -noout -checkend $((826*86400)) >/dev/null && stat -c %a $d/key.pem'
  [ "$status" -eq 0 ]; [[ "$output" == *644* ]]
}
@test "has extendedKeyUsage serverAuth (Apple requirement)" {
  run bash -c 'source lib/cert.sh; d=$(mktemp -d); cert_generate localhost $d $d/ca; openssl x509 -in $d/cert.pem -noout -ext extendedKeyUsage'
  [ "$status" -eq 0 ]; [[ "$output" == *"TLS Web Server Authentication"* ]]
}
@test "cert_needs_renewal: fresh cert no; expiring, missing or garbage cert yes" {
  run bash -c 'source lib/cert.sh; d=$(mktemp -d); cert_generate localhost $d $d/ca; cert_needs_renewal $d'
  [ "$status" -ne 0 ]
  run bash -c 'source lib/cert.sh; d=$(mktemp -d); BPP_CERT_DAYS=10 cert_generate localhost $d $d/ca; cert_needs_renewal $d'
  [ "$status" -eq 0 ]
  run bash -c 'source lib/cert.sh; d=$(mktemp -d); cert_needs_renewal $d'
  [ "$status" -eq 0 ]
  run bash -c 'source lib/cert.sh; d=$(mktemp -d); echo junk >$d/cert.pem; cert_needs_renewal $d'
  [ "$status" -eq 0 ]
}
@test "creates missing dir and sets cert mode 644" {
  run bash -c 'source lib/cert.sh; d=$(mktemp -d)/a/b; cert_generate localhost $d $d/ca && stat -c %a $d/cert.pem'
  [ "$status" -eq 0 ]; [[ "$output" == *644* ]]
}
@test "explicit 127.0.0.1 in csv does not break generation" {
  run bash -c 'source lib/cert.sh; d=$(mktemp -d); cert_generate localhost,127.0.0.1 $d $d/ca && openssl x509 -in $d/cert.pem -noout -ext subjectAltName'
  [ "$status" -eq 0 ]; [[ "$output" == *"IP Address:127.0.0.1"* ]]
}
@test "invalid csv fails and writes nothing" {
  run bash -c 'source lib/cert.sh; d=$(mktemp -d); cert_generate "localhost,evil.com" $d $d/ca; rc=$?; ls $d | wc -l; exit $rc'
  [ "$status" -eq 1 ]; [ "$output" = 0 ]
}
@test "server cert is a non-CA leaf signed by the local CA" {
  run bash -c 'source lib/cert.sh; d=$(mktemp -d); cert_generate localhost,192.168.1.50 $d $d/ca && openssl verify -CAfile $d/ca/ca.pem $d/cert.pem && cert_signed_by_ca $d $d/ca && openssl x509 -in $d/cert.pem -noout -ext basicConstraints'
  [ "$status" -eq 0 ]; [[ "$output" == *"CA:FALSE"* ]]
}
@test "CA: constrained to localhost, key 600 and outside the nginx dir, valid ~10 years" {
  run bash -c 'source lib/cert.sh; d=$(mktemp -d); cert_generate localhost $d $d/ca && stat -c %a $d/ca/ca.key $d/ca/ca.pem && ls $d && openssl x509 -in $d/ca/ca.pem -noout -subject -ext nameConstraints,basicConstraints -checkend $((3600*86400))'
  [ "$status" -eq 0 ]
  [[ "$output" == *600*644* ]]
  [[ "$output" != *ca.key* ]]
  [[ "$output" == *"BPP localhost CA"* && "$output" == *"DNS:localhost"* && "$output" == *"CA:TRUE"* && "$output" == *"pathlen:0"* ]]
}
@test "CA refuses to vouch for a real domain (name constraints)" {
  run bash -c 'source lib/cert.sh; d=$(mktemp -d); cert_ca_ensure $d/ca || exit 9
    openssl req -x509 -CA $d/ca/ca.pem -CAkey $d/ca/ca.key -newkey rsa:2048 -nodes -days 30 -subj /CN=example.com \
      -addext subjectAltName=DNS:example.com -keyout $d/k.pem -out $d/evil.pem 2>/dev/null || exit 9
    openssl verify -CAfile $d/ca/ca.pem $d/evil.pem'
  [ "$status" -ne 0 ] && [ "$status" -ne 9 ]
  [[ "$output" == *"permitted subtree violation"* ]]
}
@test "CA is reused across regenerations; replaced when key is missing or CA is expiring" {
  run bash -c 'source lib/cert.sh; d=$(mktemp -d); cert_generate localhost $d $d/ca; m=$(md5sum <$d/ca/ca.pem)
    cert_generate localhost,10.1.1.1 $d $d/ca; [ "$m" = "$(md5sum <$d/ca/ca.pem)" ] || exit 2
    rm $d/ca/ca.key; cert_signed_by_ca $d $d/ca || exit 3
    cert_ca_ensure $d/ca; [ "$m" != "$(md5sum <$d/ca/ca.pem)" ] || exit 4
    ! cert_signed_by_ca $d $d/ca || exit 5
    m=$(md5sum <$d/ca/ca.pem); BPP_CA_DAYS=10 cert_ca_ensure $d/ca; [ "$m" = "$(md5sum <$d/ca/ca.pem)" ] || exit 6
    rm -r $d/ca; BPP_CA_DAYS=10 cert_ca_ensure $d/ca; m=$(md5sum <$d/ca/ca.pem)
    cert_ca_ensure $d/ca; [ "$m" != "$(md5sum <$d/ca/ca.pem)" ] || exit 7'
  [ "$status" -eq 0 ]
}
@test "self-signed cert from an older version is not signed by the CA" {
  run bash -c 'source lib/cert.sh; d=$(mktemp -d); cert_ca_ensure $d/ca
    openssl req -x509 -newkey rsa:2048 -nodes -days 30 -subj /CN=localhost -keyout $d/key.pem -out $d/cert.pem 2>/dev/null
    cert_signed_by_ca $d $d/ca'
  [ "$status" -ne 0 ]
}
