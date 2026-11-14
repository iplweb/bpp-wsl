@test "SAN contains localhost, 127.0.0.1 and LAN ips" {
  run bash -c 'source lib/cert.sh; d=$(mktemp -d); cert_generate localhost,192.168.1.50 $d; openssl x509 -in $d/cert.pem -noout -ext subjectAltName'
  [[ "$output" == *"DNS:localhost"* && "$output" == *"IP Address:127.0.0.1"* && "$output" == *"IP Address:192.168.1.50"* ]]
}
@test "valid 825 days (Apple limit), not longer, and key readable" {
  run bash -c 'source lib/cert.sh; d=$(mktemp -d); cert_generate localhost $d; openssl x509 -in $d/cert.pem -noout -checkend $((824*86400)) >/dev/null && ! openssl x509 -in $d/cert.pem -noout -checkend $((826*86400)) >/dev/null && stat -c %a $d/key.pem'
  [ "$status" -eq 0 ]; [[ "$output" == *644* ]]
}
@test "has extendedKeyUsage serverAuth (Apple requirement)" {
  run bash -c 'source lib/cert.sh; d=$(mktemp -d); cert_generate localhost $d; openssl x509 -in $d/cert.pem -noout -ext extendedKeyUsage'
  [ "$status" -eq 0 ]; [[ "$output" == *"TLS Web Server Authentication"* ]]
}
@test "cert_needs_renewal: fresh cert no; expiring, missing or garbage cert yes" {
  run bash -c 'source lib/cert.sh; d=$(mktemp -d); cert_generate localhost $d; cert_needs_renewal $d'
  [ "$status" -ne 0 ]
  run bash -c 'source lib/cert.sh; d=$(mktemp -d); BPP_CERT_DAYS=10 cert_generate localhost $d; cert_needs_renewal $d'
  [ "$status" -eq 0 ]
  run bash -c 'source lib/cert.sh; d=$(mktemp -d); cert_needs_renewal $d'
  [ "$status" -eq 0 ]
  run bash -c 'source lib/cert.sh; d=$(mktemp -d); echo junk >$d/cert.pem; cert_needs_renewal $d'
  [ "$status" -eq 0 ]
}
@test "creates missing dir and sets cert mode 644" {
  run bash -c 'source lib/cert.sh; d=$(mktemp -d)/a/b; cert_generate localhost $d && stat -c %a $d/cert.pem'
  [ "$status" -eq 0 ]; [[ "$output" == *644* ]]
}
@test "explicit 127.0.0.1 in csv does not break generation" {
  run bash -c 'source lib/cert.sh; d=$(mktemp -d); cert_generate localhost,127.0.0.1 $d && openssl x509 -in $d/cert.pem -noout -ext subjectAltName'
  [ "$status" -eq 0 ]; [[ "$output" == *"IP Address:127.0.0.1"* ]]
}
@test "invalid csv fails and writes nothing" {
  run bash -c 'source lib/cert.sh; d=$(mktemp -d); cert_generate "localhost,evil.com" $d; rc=$?; ls $d | wc -l; exit $rc'
  [ "$status" -eq 1 ]; [ "$output" = 0 ]
}
