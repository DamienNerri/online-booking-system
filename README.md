# Système de réservation en ligne distribué

Projet EIDAL Bloc 5 — « Introduction aux traitements distribués ».
Système de réservation (hôtels / vols) réparti sur plusieurs nœuds, garantissant
l'**atomicité** des transactions et l'**absence de surréservation** malgré les
accès concurrents.

Stack : **ASP.NET Core 8 (C#)** · **PostgreSQL 16** · **Nginx** · **Docker Compose**.

## Architecture (résumé)

```
Clients → Nginx (round-robin) → 3 nœuds API (stateless) → PostgreSQL (source de vérité)
```

La cohérence et la sérialisation de la concurrence reposent sur les transactions
ACID de PostgreSQL et le verrouillage `SELECT ... FOR UPDATE`. Voir
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) pour le détail.

## Prérequis

- Docker + Docker Compose (le SDK .NET n'est **pas** requis : build dans le conteneur).

## Démarrage en une commande

```bash
docker compose up -d --build
```

Cela provisionne PostgreSQL, 3 nœuds API (migrations + seed appliqués
automatiquement au démarrage) et le répartiteur Nginx.

- **Interface web** : `http://localhost:8088/`
- **API** : `http://localhost:8088/api/...`

Le front (HTML/CSS/JS statique) est servi par Nginx sur la même origine que
l'API, donc sans problème de CORS.

Arrêt :

```bash
docker compose down          # conserve les données
docker compose down -v       # supprime aussi le volume PostgreSQL
```

## Endpoints

| Méthode | Chemin | Auth | Description |
|---|---|---|---|
| POST | `/api/auth/register` | non | Créer un compte, retourne un JWT |
| POST | `/api/auth/login` | non | Se connecter, retourne un JWT |
| GET | `/api/availability?type=&from=&to=` | non | Lister les slots disponibles (dates `yyyy-MM-dd`) |
| POST | `/api/bookings` | oui | Créer une réservation (hold) — corps `{ "slotIds": [1,2] }` |
| POST | `/api/bookings/{id}/confirm` | oui | Confirmer un hold |
| DELETE | `/api/bookings/{id}` | oui | Annuler une réservation |
| GET | `/health` | non | État du nœud |

L'authentification se fait par en-tête `Authorization: Bearer <token>`.

## Exemple rapide

```bash
# 1. Compte + token
TOKEN=$(curl -s -X POST http://localhost:8088/api/auth/register \
  -H 'Content-Type: application/json' \
  -d '{"email":"me@test.local","password":"password123"}' | jq -r .token)

# 2. Disponibilité
curl "http://localhost:8088/api/availability?type=HOTEL_ROOM&from=2026-01-01&to=2026-01-05"

# 3. Réservation
curl -X POST http://localhost:8088/api/bookings \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"slotIds":[1]}'
```

## Tests et validation

Scripts de recette (PowerShell), stack démarrée :

```powershell
powershell -File infra\smoke-test.ps1            # parcours complet
powershell -File infra\concurrency-test.ps1      # preuve anti-surréservation
powershell -File infra\fault-tolerance-test.ps1  # panne d'un nœud
powershell -File infra\security-test.ps1         # sécurité (401/403/429, injection)
```

Tests unitaires (SDK conteneurisé) :

```bash
docker run --rm -v "${PWD}:/src" -w /src mcr.microsoft.com/dotnet/sdk:8.0 dotnet test
```

Le protocole complet est décrit dans
[docs/CAHIER-DE-RECETTE.md](docs/CAHIER-DE-RECETTE.md).

## Configuration

Variables d'environnement (voir `.env.example`) : chaîne de connexion, secret JWT,
durée du hold (`Booking__HoldTtlSeconds`), rate limiting. En production, fournir
un `Jwt__Secret` long et aléatoire.

## Sécurité

- Mots de passe hachés (BCrypt), jamais stockés en clair.
- Requêtes SQL 100 % paramétrées (anti-injection).
- Authentification JWT + contrôle d'ownership sur les réservations.
- Limitation de débit par utilisateur/IP.

> Note : dans cette version pédagogique, l'API est exposée en HTTP derrière Nginx.
> En production, placer TLS au niveau du répartiteur et gérer les secrets via un
> coffre (pas de secret en clair dans le compose).
