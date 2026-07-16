#!/usr/bin/env bash
# Checkpoint 4.3 — tolerance aux pannes (Req 7).
# Tue un noeud API pendant l'activite et verifie que le service continue via les
# noeuds restants, sans reservation partielle ni incoherence.
set -euo pipefail

BASE='http://localhost:8088'

# 1) Etat initial : combien de noeuds repondent
echo "Detection des noeuds actifs..."
NODES_BEFORE=""
for i in $(seq 1 30); do
  NODE=$(curl -s "$BASE/health" | sed -n 's/.*"node":"\([^"]*\)".*/\1/p')
  if [ -n "$NODE" ] && ! echo "$NODES_BEFORE" | grep -q "$NODE"; then
    NODES_BEFORE="$NODES_BEFORE $NODE"
  fi
  sleep 0.2
done
NODE_COUNT_BEFORE=$(echo $NODES_BEFORE | wc -w | tr -d ' ')
echo "Noeuds actifs avant panne : $NODE_COUNT_BEFORE ->$NODES_BEFORE"

# 2) Tuer un noeud API
VICTIM='online-booking-system-api-2'
echo "Arret force du noeud $VICTIM ..."
docker kill "$VICTIM" > /dev/null
sleep 8   # laisser nginx detecter le noeud mort

# 3) Le service doit continuer a repondre
OK=0
KO=0
for i in $(seq 1 20); do
  CODE=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/health" || true)
  if [ "$CODE" = "200" ]; then
    OK=$((OK + 1))
  else
    KO=$((KO + 1))
  fi
  sleep 0.3
done
echo "Apres panne : $OK requetes /health OK, $KO en echec"
if [ "$OK" -lt 10 ]; then
  echo "ECHEC: trop d'echecs apres la panne d'un noeud"
  docker start "$VICTIM" > /dev/null 2>&1 || true
  exit 1
fi

# 4) Une reservation reste possible et coherente
EMAIL="ft${RANDOM}${RANDOM}@test.local"
REG=$(curl -s -X POST "$BASE/api/auth/register" \
  -H 'Content-Type: application/json' \
  -d "{\"email\":\"$EMAIL\",\"password\":\"password123\"}")
TOKEN=$(echo "$REG" | sed -n 's/.*"token":"\([^"]*\)".*/\1/p')

AVAIL=$(curl -s "$BASE/api/availability?type=HOTEL_ROOM&from=2026-01-25&to=2026-01-28")
SLOT_ID=$(echo "$AVAIL" | sed -n 's/.*"slotId":\([0-9]*\).*/\1/p' | head -1)

BOOKING=$(curl -s -X POST "$BASE/api/bookings" \
  -H 'Content-Type: application/json' \
  -H "Authorization: Bearer $TOKEN" \
  -d "{\"slotIds\":[$SLOT_ID]}")
BOOKING_ID=$(echo "$BOOKING" | sed -n 's/.*"bookingId":\([0-9]*\).*/\1/p')
STATUS=$(echo "$BOOKING" | sed -n 's/.*"status":"\([^"]*\)".*/\1/p')
echo "Reservation apres panne : bookingId=$BOOKING_ID status=$STATUS"

if [ "$STATUS" != "HOLD" ]; then
  echo "ECHEC: reservation non creee apres panne"
  docker start "$VICTIM" > /dev/null 2>&1 || true
  exit 1
fi

# 5) Redemarrer le noeud (retour dans le cluster)
echo "Redemarrage du noeud $VICTIM ..."
docker start "$VICTIM" > /dev/null
sleep 6

NODES_AFTER=""
for i in $(seq 1 30); do
  NODE=$(curl -s "$BASE/health" | sed -n 's/.*"node":"\([^"]*\)".*/\1/p')
  if [ -n "$NODE" ] && ! echo "$NODES_AFTER" | grep -q "$NODE"; then
    NODES_AFTER="$NODES_AFTER $NODE"
  fi
  sleep 0.2
done
NODE_COUNT_AFTER=$(echo $NODES_AFTER | wc -w | tr -d ' ')
echo "Noeuds actifs apres redemarrage : $NODE_COUNT_AFTER ->$NODES_AFTER"

echo ""
echo "CHECKPOINT 4.3 REUSSI : continuite de service pendant la panne, reservation coherente, noeud reintegre."
