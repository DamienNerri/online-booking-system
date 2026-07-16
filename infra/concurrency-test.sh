#!/usr/bin/env bash
# Checkpoint 3.2 — preuve d'absence de surreservation (Property 2, Req 3).
# Lance N requetes VRAIMENT paralleles sur le meme slot et verifie qu'exactement
# une seule reussit (201), toutes les autres etant rejetees en conflit (409).
set -euo pipefail

BASE='http://localhost:8088'
N=30

# 1) Utilisateur + token
EMAIL="conc${RANDOM}${RANDOM}@test.local"
REG=$(curl -s -X POST "$BASE/api/auth/register" \
  -H 'Content-Type: application/json' \
  -d "{\"email\":\"$EMAIL\",\"password\":\"password123\"}")
TOKEN=$(echo "$REG" | sed -n 's/.*"token":"\([^"]*\)".*/\1/p')

if [ -z "$TOKEN" ]; then
  echo "ECHEC: impossible d'obtenir un token. Reponse: $REG"
  exit 1
fi

# 2) Choisir un slot disponible
AVAIL=$(curl -s "$BASE/api/availability?type=HOTEL_ROOM&from=2026-01-15&to=2026-01-20")
SLOT_ID=$(echo "$AVAIL" | sed -n 's/.*"slotId":\([0-9]*\).*/\1/p' | head -1)

if [ -z "$SLOT_ID" ]; then
  echo "ECHEC: aucun slot disponible. Reponse: $AVAIL"
  exit 1
fi

echo "Slot cible = $SLOT_ID ; lancement de $N requetes concurrentes..."

# 3) Lancer N requetes en parallele, stocker les codes HTTP
TMPDIR_CONC=$(mktemp -d)
trap 'rm -rf "$TMPDIR_CONC"' EXIT

for i in $(seq 1 $N); do
  (
    CODE=$(curl -s -o /dev/null -w '%{http_code}' \
      -X POST "$BASE/api/bookings" \
      -H 'Content-Type: application/json' \
      -H "Authorization: Bearer $TOKEN" \
      -d "{\"slotIds\":[$SLOT_ID]}")
    echo "$CODE" > "$TMPDIR_CONC/$i.code"
  ) &
done
wait

# 4) Compter les codes de reponse
SUCCESS=0
CONFLICT=0
OTHER=0

for f in "$TMPDIR_CONC"/*.code; do
  CODE=$(cat "$f")
  case "$CODE" in
    201) SUCCESS=$((SUCCESS + 1)) ;;
    409) CONFLICT=$((CONFLICT + 1)) ;;
    *)   OTHER=$((OTHER + 1)) ;;
  esac
done

echo "Resultats : succes(201)=$SUCCESS  conflit(409)=$CONFLICT  autre=$OTHER"

if [ "$SUCCESS" -ne 1 ]; then
  echo "ECHEC: $SUCCESS reservations ont reussi (attendu: exactement 1) => SURRESERVATION"
  exit 1
fi
if [ "$OTHER" -ne 0 ]; then
  echo "ECHEC: $OTHER reponses inattendues"
  exit 1
fi
if [ "$CONFLICT" -ne $((N - 1)) ]; then
  echo "ECHEC: nombre de conflits inattendu ($CONFLICT au lieu de $((N - 1)))"
  exit 1
fi

echo ""
echo "CHECKPOINT 3.2 REUSSI : exactement 1 reservation, $CONFLICT conflits. Invariant anti-surreservation respecte."
