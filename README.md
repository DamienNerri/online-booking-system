# Plateforme de réservation d'hôtel — distribuée, sans surréservation

> Projet **5ESGI — Architecture Logicielle : Heuristiques et Compromis**.
> Plateforme web de réservation d'hôtel répartie sur plusieurs nœuds, garantissant
> l'**atomicité** des réservations et l'**absence de surréservation** malgré des
> accès **concurrents**.

**Stack :** ASP.NET Core 8 (C#) · PostgreSQL 16 · Nginx · Docker Compose.

---

## 📌 Pour le jury — par où commencer

Les **livrables** demandés se trouvent dans le dossier [`livrables/`](livrables/) :

| Livrable | Contenu | Fichier |
|---|---|---|
| **Dossier d'architecture** (principal) | Besoins, parcours, heuristiques & compromis, diagrammes (C4, ER, séquence, états), ADR | [`livrables/1-dossier-architecture.md`](livrables/1-dossier-architecture.md) |
| **Rapport de performance** | Scénarios critiques, **mesures réelles**, goulots, optimisations, protocole 500 users | [`livrables/2-rapport-performance.md`](livrables/2-rapport-performance.md) |
| **Rapport d'accessibilité RGAA 4** | Audit outillé (WAVE) + audit manuel de l'écran clé, non-conformités + corrections | [`livrables/3-rapport-accessibilite-rgaa4.md`](livrables/3-rapport-accessibilite-rgaa4.md) |
| **ADR** | Journal des décisions d'architecture | [`livrables/adr/ADR.md`](livrables/adr/ADR.md) |
| **Prototype** | L'application elle-même (ce dépôt) | `http://localhost:8088/` |

Index et documents de cadrage : [`livrables/README.md`](livrables/README.md) et
[`livrables/rendu/`](livrables/rendu/) (besoins fonctionnels / non fonctionnels,
parcours, **cartographie des tensions**).

---

## 🎯 Le problème résolu (le cœur du projet)

Contexte type : ouverture des réservations d'un grand hôtel pour un congrès —
**500 clients simultanés**, dont deux qui visent **la même chambre au même
instant**. Le défi est architectural : garantir, en environnement **distribué**
et **concurrent**, deux propriétés non négociables :

- **Atomicité** — une réservation multi-chambres est tout ou rien ;
- **Zéro surréservation** — une chambre-nuit n'est attribuée qu'à une seule
  réservation active.

**Solution — défense en profondeur (2 niveaux) :**
1. **`SELECT … FOR UPDATE`** : sérialise les transactions concurrentes sur un même
   slot (la 2ᵉ échoue proprement en `409`).
2. **Index unique partiel** `uq_active_slot (slot_id) WHERE active` : garde-fou
   ultime, rend une double réservation active physiquement impossible.
3. Le tout dans une **transaction ACID** PostgreSQL.

**Preuve mesurée** (voir Livrable 2) : 6 requêtes concurrentes sur le même slot →
**1× `201`** + **5× `409`**, et la base ne contient **qu'une** réservation active.
Tests automatisés : 50 clients sur 1 chambre → **1 succès, 49 conflits**.

---

## 🏗️ Architecture (résumé)

```
Clients → Nginx (round-robin) → 3 nœuds API stateless → PostgreSQL (source de vérité)
```

- **Monolithe modulaire** (couches Endpoints / Services / Repositories) déployé en
  **réplicas stateless** — choix justifié par les heuristiques du cours (équipe de
  3, domaine simple, latence critique, ACID natif ; microservices écartés).
- La **cohérence** et la **sérialisation de la concurrence** sont déléguées aux
  transactions ACID de PostgreSQL — pas de consensus maison.
- Positionnement **CAP → CP** : en cas de doute, **refuser proprement** plutôt que
  risquer une incohérence.

Détail complet (vues 4+1, modèle de données, chemins critiques annotés,
compromis ATAM/QOC, ADR) : [`livrables/1-dossier-architecture.md`](livrables/1-dossier-architecture.md).

---

## 🚀 Démarrage en une commande

**Prérequis :** Docker + Docker Compose (le SDK .NET n'est *pas* requis : build
dans le conteneur).

```bash
docker compose up -d --build
```

Provisionne PostgreSQL, 3 nœuds API (migrations + seed appliqués automatiquement)
et le répartiteur Nginx.

- **Interface web** : `http://localhost:8088/`
- **API** : `http://localhost:8088/api/...`
- **Santé du nœud** : `http://localhost:8088/health`

Le front (HTML/CSS/JS statique) est servi par Nginx sur la même origine que l'API
(pas de CORS). Le jeu de démonstration couvre un **horizon glissant de 365 nuits**
à partir du jour de lancement (toute date de l'année à venir est réservable).

Arrêt :

```bash
docker compose down          # conserve les données
docker compose down -v       # supprime aussi le volume PostgreSQL
```

---

## 🔌 Endpoints

| Méthode | Chemin | Auth | Description |
|---|---|---|---|
| POST | `/api/auth/register` | non | Créer un compte, retourne un JWT |
| POST | `/api/auth/login` | non | Se connecter, retourne un JWT |
| GET | `/api/availability?type=&from=&to=` | non | Lister les chambres disponibles (dates `yyyy-MM-dd`) |
| POST | `/api/bookings` | oui | Créer une réservation (hold) — corps `{ "slotIds": [1,2] }` |
| GET | `/api/bookings` | oui | Lister ses réservations |
| POST | `/api/bookings/{id}/confirm` | oui | Confirmer un hold |
| DELETE | `/api/bookings/{id}` | oui | Annuler une réservation |
| GET | `/health` | non | État du nœud (renvoie l'identifiant du nœud) |

Authentification par en-tête `Authorization: Bearer <token>`.

### Exemple rapide

```bash
# 1. Compte + token
TOKEN=$(curl -s -X POST http://localhost:8088/api/auth/register \
  -H 'Content-Type: application/json' \
  -d '{"email":"me@test.local","password":"password123"}' | jq -r .token)

# 2. Disponibilité (dates dans le futur — les dates passées sont refusées)
curl "http://localhost:8088/api/availability?type=HOTEL_ROOM&from=2026-09-20&to=2026-09-22"

# 3. Réservation (remplacer 1 par un slotId réel renvoyé ci-dessus)
curl -X POST http://localhost:8088/api/bookings \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"slotIds":[1]}'
```

---

## ✅ Tests et validation

**Tests automatisés** (dont les tests de concurrence sur PostgreSQL réel) :

```bash
docker compose up -d --build     # démarre notamment PostgreSQL
dotnet test                      # 6/6 attendus (4 concurrence + 2 smoke)
```

> Sans SDK .NET local, exécuter les tests dans un conteneur :
> ```bash
> docker run --rm -v "${PWD}:/src" -w /src mcr.microsoft.com/dotnet/sdk:8.0 dotnet test
> ```

**Scripts de recette** (stack démarrée) — versions `.sh` et `.ps1` dans
[`infra/`](infra/) : parcours complet, **preuve anti-surréservation**, panne d'un
nœud, sécurité (401/403/429, injection).

---

## 🔒 Sécurité

- Mots de passe hachés **BCrypt**, jamais stockés en clair.
- Requêtes SQL **100 % paramétrées** (anti-injection).
- **JWT** + contrôle d'**ownership** sur les réservations (anti-IDOR).
- **Rate limiting** par utilisateur/IP (429 au-delà du seuil).
- **Secret JWT hors code**, **validé au démarrage** (l'API refuse de démarrer si
  le secret est vide) : `dotnet user-secrets` en dev, variable `Jwt__Secret` en
  conteneur.

> Version pédagogique : l'API est exposée en HTTP derrière Nginx. En production,
> terminer le **TLS** au répartiteur et gérer les secrets via un coffre.

---

## ⚙️ Configuration

Variables d'environnement (voir [`.env.example`](.env.example)) : chaîne de
connexion, secret JWT, durée du hold (`Booking__HoldTtlSeconds`), rate limiting.

---

## 🗂️ Structure du dépôt

```
src/            API ASP.NET Core (Endpoints / Services / Repositories / Workers / Middleware)
migrations/     Schéma (001_init.sql) + jeu de démo glissant 365 nuits (002_seed.sql)
frontend/       Front statique (HTML sémantique, XSS-safe) servi par Nginx
infra/          nginx.conf + scripts de recette (.sh / .ps1)
tests/          xUnit — SmokeTests + ConcurrencyTests (base PostgreSQL réelle)
livrables/      Livrables 5ESGI (architecture, performance, RGAA 4, ADR, rendu/)
docker-compose.yml · Dockerfile · OnlineBooking.sln
```
