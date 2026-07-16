#!/usr/bin/env bash
# Smoke test end-to-end via le repartiteur Nginx.
set -euo pipefail

BASE='http://localhost:8088'

echo "1) Inscription"
EMAIL="user${RANDOM}${RANDOM}@test.local"
REG=$(curl -s -X POST "$BASE/api/auth/register" \
  -H 'Content-Type: application/json' \
  -d "{\"email\":\"$EMAIL\",\"password\":\"password123\"}")
TOKEN=$(echo "$REG" | sed -n 's/.*"token":"\([^"]*\)".*/\1/p')
ROLE=$(echo "$REG" | sed -n 's/.*"role":"\([^"]*\)".*/\1/p')

if [ -z "$TOKEN" ]; then
  echo "ECHEC: impossible d'obtenir un token. Reponse: $REG"
  exit 1
fi
echo "   token obtenu, role=$ROLE"

echo "2) Disponibilite"
AVAIL=$(curl -s "$BASE/api/availability?type=HOTEL_ROOM&from=2026-01-01&to=2026-01-05")
SLOT_COUNT=$(echo "$AVAIL" | grep -o '"slotId"' | wc -l | tr -d ' ')
SLOT_ID=$(echo "$AVAIL" | sed -n 's/.*"slotId":\([0-9]*\).*/\1/p' | head -1)

if [ -z "$SLOT_ID" ]; then
  echo "ECHEC: aucun slot disponible. Reponse: $AVAIL"
  exit 1
fi
echo "   $SLOT_COUNT slots disponibles"

echo "3) Reservation (hold) du slot $SLOT_ID"
BOOKING=$(curl -s -X POST "$BASE/api/bookings" \
  -H 'Content-Type: application/json' \
  -H "Authorization: Bearer $TOKEN" \
  -d "{\"slotIds\":[$SLOT_ID]}")
BOOKING_ID=$(echo "$BOOKING" | sed -n 's/.*"bookingId":\([0-9]*\).*/\1/p')
STATUS=$(echo "$BOOKING" | sed -n 's/.*"status":"\([^"]*\)".*/\1/p')
EXPIRES=$(echo "$BOOKING" | sed -n 's/.*"expiresAt":"\([^"]*\)".*/\1/p')

if [ -z "$BOOKING_ID" ]; then
  echo "ECHEC: reservation non creee. Reponse: $BOOKING"
  exit 1
fi
echo "   bookingId=$BOOKING_ID status=$STATUS expiresAt=$EXPIRES"

echo "4) Confirmation"
CONFIRMED=$(curl -s -X POST "$BASE/api/bookings/$BOOKING_ID/confirm" \
  -H "Authorization: Bearer $TOKEN")
CONF_STATUS=$(echo "$CONFIRMED" | sed -n 's/.*"status":"\([^"]*\)".*/\1/p')
echo "   status=$CONF_STATUS"

echo "5) Le slot ne doit plus etre disponible"
AVAIL2=$(curl -s "$BASE/api/availability?type=HOTEL_ROOM&from=2026-01-01&to=2026-01-05")
if echo "$AVAIL2" | grep -q "\"slotId\":$SLOT_ID[^0-9]"; then
  echo "ECHEC: le slot reserve est encore liste comme disponible"
  exit 1
fi
SLOT_COUNT2=$(echo "$AVAIL2" | grep -o '"slotId"' | wc -l | tr -d ' ')
echo "   OK: slot retire des disponibilites ($SLOT_COUNT2 restants)"

echo "6) Annulation"
curl -s -X DELETE "$BASE/api/bookings/$BOOKING_ID" \
  -H "Authorization: Bearer $TOKEN" > /dev/null
AVAIL3=$(curl -s "$BASE/api/availability?type=HOTEL_ROOM&from=2026-01-01&to=2026-01-05")
if ! echo "$AVAIL3" | grep -q "\"slotId\":$SLOT_ID[^0-9]"; then
  echo "ECHEC: le slot annule n'est pas redevenu disponible"
  exit 1
fi
echo "   OK: slot redevenu disponible apres annulation"

echo ""
echo "SMOKE TEST REUSSI"
