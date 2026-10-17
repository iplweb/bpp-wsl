@test "emit produces one JSON line with numeric progress" {
  run bash -c 'source lib/events.sh; emit pull 0.5 "Pobieranie \"x\""'
  [ "$(echo "$output" | jq -r .step)" = pull ]
  [ "$(echo "$output" | jq .progress)" = 0.5 ]
  [ "$(echo "$output" | jq -r .message)" = 'Pobieranie "x"' ]
}
@test "emit null progress" { run bash -c 'source lib/events.sh; emit migrate null "Czekam"'; [ "$(echo "$output" | jq .progress)" = null ]; }
