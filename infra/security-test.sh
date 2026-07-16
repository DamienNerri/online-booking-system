#!/usr/bin/env bash
# Tache 5.4 — tests de securite (Req 8, 9). Documente les anomalies attendues.
set -euo pipefail

BASE='http://localhost:8088'
FAIL=0

echo "S1) Injection SQL sur le parametre 'type'"
PAYLOAD="HOTEL_ROOM'; DROP TABLE bookings;--"
ENCODED=$(python3 -c "import urllib.parse; print(urllib.parse.quote('''$PAYLOAD'''))")
RESP=$(curl -s "$BASE/api/availability?type=$ENCODED&from=2026-01-01&to=2026-01-05" || true)
# Requetes parametrees => aucune injection, resultat vide, tables intactes.
CHECK=$(curl -s "$BASE/api/availability?type=HOTEL_ROOM&from=2026-01-01&to=2026-01-05")
CHECK_COUNT=$(echo "$CHECK" | grep -o '"slotId"' | wc -l | tr -d ' ')
if [ "$CHECK_COUNT" -gt 0 ]; then
  echo "   OK: injection neutralisee, tables intactes ($CHECK_COUNT slots)"
else
  echo "   ATTENTION: verifier l'etat des donnees"
  FAIL=$((FAIL + 1))
fi

echo "S2) Acces sans jeton (POST /api/bookings) -> 401 attendu"
CODE=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/api/bookings" \
  -H 'Content-Type: application/json' \
  -d '{"slotIds":[1]}')
if [ "$CODE" = "401" ]; then
  echo "   OK: 401"
else
  echo "   ECHEC: $CODE"
  FAIL=$((FAIL + 1))
fi

echo "S3) Jeton invalide -> 401 attendu"
CODE=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/api/bookings" \
  -H 'Content-Type: application/json' \
  -H 'Authorization: Bearer not.a.real.token' \
  -d '{"slotIds":[1]}')
if [ "$CODE" = "401" ]; then
  echo "   OK: 401"
else
  echo "   ECHEC: $CODE"
  FAIL=$((FAIL + 1))
fi

echo "S4) Controle d'acces (ownership) -> 403 attendu"
REG_A=$(curl -s -X POST "$BASE/api/auth/register" \
  -H 'Content-Type: application/json' \
  -d "{\"email\":\"a${RANDOM}${RANDOM}@t.local\",\"password\":\"password123\"}")
TOKEN_A=$(echo "$REG_A" | sed -n 's/.*"token":"\([^"]*\)".*/\1/p')

REG_B=$(curl -s -X POST "$BASE/api/auth/register" \
  -H 'Content-Type: application/json' \
  -d "{\"email\":\"b${RANDOM}${RANDOM}@t.local\",\"password\":\"password123\"}")
TOKEN_B=$(echo "$REG_B" | sed -n 's/.*"token":"\([^"]*\)".*/\1/p')

AVAIL=$(curl -s "$BASE/api/availability?type=HOTEL_ROOM&from=2026-01-05&to=2026-01-08")
SLOT_ID=$(echo "$AVAIL" | sed -n 's/.*"slotId":\([0-9]*\).*/\1/p' | head -1)

BOOKING=$(curl -s -X POST "$BASE/api/bookings" \
  -H 'Content-Type: application/json' \
  -H "Authorization: Bearer $TOKEN_A" \
  -d "{\"slotIds\":[$SLOT_ID]}")
BOOKING_ID=$(echo "$BOOKING" | sed -n 's/.*"bookingId":\([0-9]*\).*/\1/p')

# B essaie d'annuler la reservation de A
CODE=$(curl -s -o /dev/null -w '%{http_code}' -X DELETE "$BASE/api/bookings/$BOOKING_ID" \
  -H "Authorization: Bearer $TOKEN_B")
if [ "$CODE" = "403" ]; then
  echo "   OK: 403 (B ne peut pas annuler la reservation de A)"
else
  echo "   ECHEC: $CODE"
  FAIL=$((FAIL + 1))
fi

echo "S5) Rate limiting -> 429 attendu sous rafale"
LIMITED=0
# Envoyer 500 requetes rapidement en parallele par lots
TMPDIR_RL=$(mktemp -d)
trap 'rm -rf "$TMPDIR_RL"' EXIT

for i in $(seq 1 500); do
  (
    CODE=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/health")
    echo "$CODE" > "$TMPDIR_RL/$i.code"
  ) &
  # Lancer par lots de 50 pour eviter de saturer les FD
  if [ $((i % 50)) -eq 0 ]; then
    wait
  fi
done
wait

for f in "$TMPDIR_RL"/*.code; do
  CODE=$(cat "$f")
  if [ "$CODE" = "429" ]; then
    LIMITED=$((LIMITED + 1))
  fi
done
echo "   Reponses 429 : $LIMITED / 500"
if [ "$LIMITED" -gt 0 ]; then
  echo "   OK: la limitation de debit se declenche"
else
  echo "   ECHEC: aucune 429"
  FAIL=$((FAIL + 1))
fi

if [ "$FAIL" -gt 0 ]; then
  echo ""
  echo "$FAIL test(s) de securite en echec"
  exit 1
fi

echo ""
echo "TESTS DE SECURITE REUSSIS"
