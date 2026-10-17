@test "SAN contains localhost, 127.0.0.1 and LAN ips" {
  run bash -c 'source lib/cert.sh; d=$(mktemp -d); cert_generate localhost,192.168.1.50 $d; openssl x509 -in $d/cert.pem -noout -ext subjectAltName'
  [[ "$output" == *"DNS:localhost"* && "$output" == *"IP Address:127.0.0.1"* && "$output" == *"IP Address:192.168.1.50"* ]]
}
@test "valid ~10 years and key readable" {
  run bash -c 'source lib/cert.sh; d=$(mktemp -d); cert_generate localhost $d; openssl x509 -in $d/cert.pem -noout -checkend $((3600*24*3600)) && stat -c %a $d/key.pem'
  [ "$status" -eq 0 ]; [[ "$output" == *644* ]]
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
