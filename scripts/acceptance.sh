#!/usr/bin/env bash
# Black-box acceptance checks against a RUNNING stack (needs only bash + curl).
#
#   docker compose up --build -d
#   ./scripts/acceptance.sh                 # defaults to http://localhost:8080
#   ./scripts/acceptance.sh http://host:8080
#
# Optional expiry check (waits for a real hold to expire; start the stack with a short hold first):
#   HOLD_DURATION_SECONDS=15 EXPIRATION_SWEEP_INTERVAL_SECONDS=2 docker compose up -d backend
#   EXPIRY_TEST=1 ./scripts/acceptance.sh
#
# The script creates its own classes and students (unique per run) and never deletes anything.
set -u

BASE="${1:-http://localhost:8080}"
RUN_ID="$(date +%s)"
PASS=0
FAIL=0
TMP="$(mktemp)"
trap 'rm -f "$TMP"' EXIT

STATUS=""
BODY=""

# req METHOD PATH [student-id] [json-body]  -> sets STATUS and BODY
req() {
  local method="$1" path="$2" student="${3:-}" data="${4:-}"
  local args=(-s -o "$TMP" -w '%{http_code}' -X "$method" "$BASE$path")
  [ -n "$student" ] && args+=(-H "X-Student-Id: $student")
  [ -n "$data" ] && args+=(-H 'Content-Type: application/json' -d "$data")
  STATUS="$(curl "${args[@]}")"
  BODY="$(cat "$TMP")"
}

# field NAME  -> first value of "NAME" in $BODY (string or number)
field() { printf '%s' "$BODY" | sed -n "s/.*\"$1\":\"\{0,1\}\([^\",}]*\).*/\1/p" | head -n1; }

check() { # description expected actual
  if [ "$2" = "$3" ]; then
    PASS=$((PASS + 1)); printf '  PASS  %s\n' "$1"
  else
    FAIL=$((FAIL + 1)); printf '  FAIL  %s (expected %s, got %s)\n' "$1" "$2" "$3"
  fi
}

student() { printf '%s%07d' "$RUN_ID" "$1"; } # 10 + 7 = 17 digits, unique per run

echo "Acceptance run $RUN_ID against $BASE"

echo "[1] Frontend and routing"
req GET /;                     check "GET / serves the app" 200 "$STATUS"
req GET /dashboard;            check "SPA route /dashboard falls back to index.html" 200 "$STATUS"
req GET /api/health;           check "GET /api/health reaches the backend" 200 "$STATUS"
req GET /health;               check "GET /health reaches the backend" 200 "$STATUS"

echo "[2] API 1-3: classes"
req POST /classes "" "{\"name\":\"Acceptance A $RUN_ID\",\"totalSeats\":3}"
check "POST /classes -> 201" 201 "$STATUS"
CLASS_A="$(field id)"
check "response has name and totalSeats" "3" "$(field totalSeats)"
req POST /api/classes "" "{\"name\":\"Acceptance B $RUN_ID\",\"totalSeats\":2}"
check "POST /api/classes (prefixed route) -> 201" 201 "$STATUS"
CLASS_B="$(field id)"
req POST /classes "" '{"name":"","totalSeats":5}';                  check "blank name -> 400" 400 "$STATUS"
req POST /classes "" '{"name":"X","totalSeats":0}';                 check "zero seats -> 400" 400 "$STATUS"
req POST /classes "" '{"name":"X","totalSeats":-4}';                check "negative seats -> 400" 400 "$STATUS"
req POST /classes "" '{not json';                                   check "malformed JSON -> 400 (not 500)" 400 "$STATUS"
req POST /classes "" "{\"name\":\"ACCEPTANCE A $RUN_ID\",\"totalSeats\":3}"
check "duplicate name (case-insensitive) -> 409" 409 "$STATUS"
req GET /classes;              check "GET /classes -> 200" 200 "$STATUS"
case "$BODY" in *"$CLASS_A"*) check "list contains the new class" yes yes ;; *) check "list contains the new class" yes no ;; esac
req GET "/classes/$CLASS_A";   check "GET /classes/{id} -> 200" 200 "$STATUS"
check "availableSeats = 3" 3 "$(field availableSeats)"
req GET "/classes/00000000-0000-0000-0000-000000000000"; check "unknown class -> 404" 404 "$STATUS"
req GET /classes/not-a-guid;   check "malformed id -> 400" 400 "$STATUS"

echo "[3] API 4-8: hold lifecycle"
S1="$(student 1)"; S2="$(student 2)"; S3="$(student 3)"
req POST "/classes/$CLASS_A/holds";                check "apply without header -> 400" 400 "$STATUS"
req POST "/classes/$CLASS_A/holds" "12345";        check "apply with invalid student id -> 400" 400 "$STATUS"
req POST "/classes/$CLASS_A/holds" "$S1"
check "apply -> 201" 201 "$STATUS"
HOLD1="$(field holdId)"
check "status is HELD" HELD "$(field status)"
check "expiresAt is returned (UTC)" 1 "$(printf '%s' "$BODY" | grep -c 'expiresAt":"[0-9T:.-]*Z"')"
req GET "/classes/$CLASS_A";                       check "heldSeats = 1" 1 "$(field heldSeats)"
req GET "/holds/$HOLD1";                           check "GET /holds/{id} -> 200" 200 "$STATUS"
check "hold status HELD" HELD "$(field status)"
req GET "/holds/00000000-0000-0000-0000-000000000000"; check "unknown hold -> 404" 404 "$STATUS"
req GET /me/holds "$S1";                           check "GET /me/holds -> 200" 200 "$STATUS"
check "includes className" 1 "$(printf '%s' "$BODY" | grep -c "Acceptance A $RUN_ID")"
req GET /me/holds "$(student 99)";                 check "other student sees []" "[]" "$BODY"
req POST "/classes/$CLASS_B/holds" "$S1";          check "second active hold for same student -> 409" 409 "$STATUS"
check "code ACTIVE_HOLD_EXISTS" ACTIVE_HOLD_EXISTS "$(field code)"
req POST "/holds/$HOLD1/confirm" "$S2";            check "confirm by another student -> 403" 403 "$STATUS"
req POST "/holds/$HOLD1/confirm" "$S1";            check "confirm (simulated payment) -> 200" 200 "$STATUS"
check "status CONFIRMED" CONFIRMED "$(field status)"
req POST "/holds/$HOLD1/confirm" "$S1";            check "repeat confirm is idempotent -> 200" 200 "$STATUS"
req POST "/holds/$HOLD1/confirm" "$S1";            check "repeat confirm again -> 200" 200 "$STATUS"
req GET "/classes/$CLASS_A"
check "confirmedSeats stays 1" 1 "$(field confirmedSeats)"
check "heldSeats back to 0" 0 "$(field heldSeats)"
req DELETE "/holds/$HOLD1" "$S1";                  check "cancel a confirmed admission -> 409" 409 "$STATUS"
req POST "/classes/$CLASS_B/holds" "$S1";          check "confirmed student may apply elsewhere -> 201" 201 "$STATUS"
HOLD_B="$(field holdId)"
req DELETE "/holds/$HOLD_B" "$S2";                 check "cancel by another student -> 403" 403 "$STATUS"
req DELETE "/holds/$HOLD_B" "$S1";                 check "cancel own hold -> 204" 204 "$STATUS"
req DELETE "/holds/$HOLD_B" "$S1";                 check "cancel again is idempotent -> 204" 204 "$STATUS"
req GET "/holds/$HOLD_B";                          check "hold reported CANCELLED" CANCELLED "$(field status)"
req GET "/classes/$CLASS_B";                       check "cancelled seat released (available 2)" 2 "$(field availableSeats)"
req POST "/classes/$CLASS_B/holds" "$S1";          check "re-apply to a cancelled class -> 409" 409 "$STATUS"
check "code ALREADY_APPLIED" ALREADY_APPLIED "$(field code)"
req POST "/holds/$HOLD_B/confirm" "$S1";           check "confirm a cancelled hold -> 409" 409 "$STATUS"
req GET /me/holds "$S1"
check "my applications list both records" 2 "$(printf '%s' "$BODY" | grep -o '"holdId"' | wc -l | tr -d ' ')"

echo "[4] Overbooking: 120 simultaneous applicants, 10 seats"
req POST /classes "" "{\"name\":\"Acceptance Burst $RUN_ID\",\"totalSeats\":10}"
BURST="$(field id)"
export BASE BURST RUN_ID
RESULTS="$(seq 1000 1119 | xargs -P 120 -I{} sh -c \
  'curl -s -o /dev/null -w "%{http_code}\n" -X POST "$BASE/classes/$BURST/holds" -H "X-Student-Id: $(printf "%s%07d" "$RUN_ID" {})"')"
CREATED="$(printf '%s\n' "$RESULTS" | grep -c '^201$')"
CONFLICT="$(printf '%s\n' "$RESULTS" | grep -c '^409$')"
OTHER="$(printf '%s\n' "$RESULTS" | grep -vc -e '^201$' -e '^409$')"
check "exactly 10 reservations created (201)" 10 "$CREATED"
check "110 rejected with 409" 110 "$CONFLICT"
check "no other status codes (no 5xx)" 0 "$OTHER"
req GET "/classes/$BURST"
check "heldSeats = 10" 10 "$(field heldSeats)"
check "availableSeats = 0 (never negative)" 0 "$(field availableSeats)"

echo "[5] Expiration"
if [ "${EXPIRY_TEST:-0}" = "1" ]; then
  req POST /classes "" "{\"name\":\"Acceptance Expiry $RUN_ID\",\"totalSeats\":1}"
  CLASS_E="$(field id)"
  req POST "/classes/$CLASS_E/holds" "$(student 5001)"
  HOLD_E="$(field holdId)"
  check "hold created" 201 "$STATUS"
  req POST "/classes/$CLASS_E/holds" "$(student 5002)"
  check "class full while the hold is active -> 409" 409 "$STATUS"
  echo "  ... waiting for the hold to expire (up to 120 s)"
  EXPIRED_STATUS=""
  for _ in $(seq 1 120); do
    req GET "/holds/$HOLD_E"; EXPIRED_STATUS="$(field status)"
    [ "$EXPIRED_STATUS" = "EXPIRED" ] && break
    sleep 1
  done
  check "hold becomes EXPIRED by itself" EXPIRED "$EXPIRED_STATUS"
  req POST "/holds/$HOLD_E/confirm" "$(student 5001)"
  check "confirming an expired hold -> 409" 409 "$STATUS"
  check "code HOLD_EXPIRED" HOLD_EXPIRED "$(field code)"
  req GET "/classes/$CLASS_E"
  check "seat is available again" 1 "$(field availableSeats)"
  req POST "/classes/$CLASS_E/holds" "$(student 5002)"
  check "another student can now take the seat -> 201" 201 "$STATUS"
else
  echo "  SKIP  set EXPIRY_TEST=1 with a short HOLD_DURATION_SECONDS (see header) to run this section"
fi

echo
echo "Result: $PASS passed, $FAIL failed"
[ "$FAIL" -eq 0 ]
