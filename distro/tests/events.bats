@test "emit produces one JSON line with numeric progress" {
  run bash -c 'source lib/events.sh; emit pull 0.5 "Pobieranie \"x\""'
  [ "$(echo "$output" | jq -r .step)" = pull ]
  [ "$(echo "$output" | jq .progress)" = 0.5 ]
  [ "$(echo "$output" | jq -r .message)" = 'Pobieranie "x"' ]
}
@test "emit null progress" { run bash -c 'source lib/events.sh; emit migrate null "Czekam"'; [ "$(echo "$output" | jq .progress)" = null ]; }
@test "emit without item has no item field" { run bash -c 'source lib/events.sh; emit pull 0.5 "x"'; [ "$(echo "$output" | jq 'has("item")')" = false ]; }
@test "emit with item adds numeric item" { run bash -c 'source lib/events.sh; emit pull 0.5 "x" 0.25'; [ "$(echo "$output" | jq .item)" = 0.25 ]; }
