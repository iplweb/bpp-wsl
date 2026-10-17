@test "single localhost -> HOSTNAME mode" {
  run bash -c 'source lib/hosts.sh; hosts_render_env localhost'
  [[ "$output" == *"DJANGO_BPP_HOSTNAME=localhost"* ]]
  [[ "$output" != *"DJANGO_BPP_HOSTNAMES"* ]]
  [[ "$output" == *"DJANGO_BPP_CSRF_EXTRA_ORIGINS=https://localhost:8443"* ]]
}
@test "LAN ips -> HOSTNAMES mode with ports in CSRF" {
  run bash -c 'source lib/hosts.sh; hosts_render_env localhost,192.168.1.50,10.0.0.7'
  [[ "$output" == *"DJANGO_BPP_HOSTNAMES=localhost,192.168.1.50,10.0.0.7"* ]]
  [[ "$output" != *"DJANGO_BPP_HOSTNAME="* ]]
  [[ "$output" == *"DJANGO_BPP_CSRF_EXTRA_ORIGINS=https://localhost:8443,https://192.168.1.50:8443,https://10.0.0.7:8443"* ]]
}
@test "validate rejects junk" {
  run bash -c 'source lib/hosts.sh; hosts_validate "localhost,evil.com"'; [ "$status" -ne 0 ]
  run bash -c 'source lib/hosts.sh; hosts_validate "localhost,1.2.3.4;rm"'; [ "$status" -ne 0 ]
  run bash -c 'source lib/hosts.sh; hosts_validate "localhost,192.168.0.1"'; [ "$status" -eq 0 ]
}
