# Design Document

## Overview

Le système est une architecture distribuée composée de plusieurs **nœuds applicatifs stateless** derrière un **répartiteur de charge**, partageant un **stockage transactionnel** (PostgreSQL) qui joue le rôle de source de vérité et de point de coordination pour la concurrence.

Le choix central : déléguer l'atomicité et la sérialisation des accès concurrents à des **transactions ACID + verrouillage au niveau ligne** dans PostgreSQL, plutôt que d'implémenter un protocole de consensus maison. C'est robuste, éprouvé, et cohérent avec un projet pédagogique. La dimension « distribuée » vient de la multiplicité des nœuds applicatifs concurrents et d'un mécanisme optionnel de verrou distribué (advisory locks / Redis).

### Stack technique retenue
| Couche | Choix | Justification |
|---|---|---|
| Service applicatif | ASP.NET Core 8 (Web API, C#) | performant, stateless, scalable horizontalement |
| Accès données | Npgsql (driver PostgreSQL) | contrôle explicite des transactions et du `FOR UPDATE` |
| Stockage / coordination | PostgreSQL 16 | transactions ACID, verrous ligne, contraintes d'unicité |
| Verrou / hold | PostgreSQL (`SELECT ... FOR UPDATE`) + colonne d'expiration | sérialisation des accès concurrents |
| Répartiteur de charge | Nginx | round-robin, health checks |
| Déploiement | Docker Compose | mise en place automatisée en une commande |
| Cache / rate limiting (option) | Redis | limitation de débit, verrou distribué optionnel |

> Note : l'invariant important (Req 3) tient tant que la coordination passe par un point transactionnel unique (PostgreSQL). Npgsql est utilisé en direct (plutôt qu'un ORM) pour garder la maîtrise du verrouillage `FOR UPDATE`.

---

## Architecture

```
                    ┌──────────────┐
   Clients  ─────►  │   Nginx LB   │  (round-robin + health check)
                    └──────┬───────┘
              ┌────────────┼────────────┐
              ▼            ▼            ▼
        ┌─────────┐  ┌─────────┐  ┌─────────┐
        │ API n°1 │  │ API n°2 │  │ API n°3 │   (stateless, scalables)
        └────┬────┘  └────┬────┘  └────┬────┘
             └───────────┼────────────┘
                         ▼
              ┌────────────────────┐
              │   PostgreSQL       │  source de vérité
              │  (ACID + FOR UPDATE)│  coordination concurrence
              └────────────────────┘
                         ▲
                   ┌─────┴─────┐
                   │  Redis    │  (rate limit / verrou optionnel)
                   └───────────┘
```

### Flux — création de réservation (chemin critique concurrence)

```
Client → LB → API node
  BEGIN TRANSACTION
    SELECT * FROM resource_slots
      WHERE id = ANY($ids) AND period = $p
      FOR UPDATE                      -- verrouille les lignes visées
    vérifier: toutes disponibles ?
      NON → ROLLBACK → 409 Conflict
      OUI → INSERT booking (status=HOLD, expires_at=now()+5min)
            UPDATE resource_slots SET status=HELD
  COMMIT
  → 201 Created { bookingId }
```

Le `FOR UPDATE` sérialise les transactions concurrentes portant sur les mêmes lignes : la deuxième transaction attend la fin de la première, puis constate l'indisponibilité et échoue proprement. C'est le mécanisme qui garantit le Requirement 3.

---

## Components and Interfaces

### 1. Service API (nœud applicatif)
Endpoints REST :
- `GET  /api/availability?type=&from=&to=` → Req 1
- `POST /api/bookings` (crée un hold) → Req 2, 3, 4
- `POST /api/bookings/{id}/confirm` → Req 4
- `DELETE /api/bookings/{id}` (annulation) → Req 5
- `GET  /health` → Req 11
- Middleware : auth JWT (Req 8), validation d'entrée (Req 9), rate limiting (Req 9).

### 2. Couche d'accès aux données (Repository)
- `findAvailable(type, period)` — requête paramétrée.
- `reserve(userId, resourceIds, period)` — transaction `FOR UPDATE` + insert.
- `confirm(bookingId, userId)` — hold → confirmé.
- `cancel(bookingId, userId)` — libération atomique.
- `releaseExpiredHolds()` — appelé par un worker périodique (Req 4).

### 3. Worker d'expiration
Tâche planifiée (chaque minute) qui exécute `releaseExpiredHolds()` : `UPDATE bookings SET status=EXPIRED WHERE status=HOLD AND expires_at < now()` puis libération des slots.

### 4. Infrastructure (IaC)
`docker-compose.yml` orchestrant : `nginx`, `api` (répliqué `--scale api=3`), `postgres`, `redis`, `migrate` (job d'initialisation du schéma). → Req 10.

## Data Models

Schéma relationnel (source de vérité et point de coordination de la concurrence) :

```sql
CREATE TABLE resource_slots (
  id           BIGSERIAL PRIMARY KEY,
  resource_id  BIGINT NOT NULL,
  period_start DATE  NOT NULL,
  period_end   DATE  NOT NULL,
  status       TEXT  NOT NULL DEFAULT 'AVAILABLE', -- AVAILABLE|HELD|BOOKED
  UNIQUE (resource_id, period_start, period_end)   -- invariant unicité (Req 3)
);

CREATE TABLE bookings (
  id          BIGSERIAL PRIMARY KEY,
  user_id     BIGINT NOT NULL,
  status      TEXT   NOT NULL, -- HOLD|CONFIRMED|CANCELLED|EXPIRED
  expires_at  TIMESTAMPTZ,
  created_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE booking_slots (
  booking_id BIGINT REFERENCES bookings(id),
  slot_id    BIGINT REFERENCES resource_slots(id),
  PRIMARY KEY (booking_id, slot_id),
  UNIQUE (slot_id)  -- un slot ne peut appartenir qu'à une réservation active
);
```

## Correctness Properties

### Property 1: Atomicité transactionnelle
Toute réservation portant sur N ressources est soit entièrement persistée, soit pas du tout ; aucun état partiel n'est visible.
**Validates: Requirements 2.1, 7.2**

### Property 2: Absence de surréservation (invariant clé)
À tout instant, pour un `slot_id` donné, il existe au plus une réservation active (HOLD ou CONFIRMED). Garanti par la contrainte `UNIQUE(slot_id)` sur `booking_slots` + le verrouillage `FOR UPDATE`.
**Validates: Requirements 3.1, 3.3**

### Property 3: Sérialisation des accès concurrents
Deux transactions visant le même slot ne peuvent réussir toutes les deux ; l'une attend et échoue.
**Validates: Requirements 3.1, 6.4**

### Property 4: Libération garantie des holds
Tout hold non confirmé est libéré au plus tard `hold_ttl` après sa création.
**Validates: Requirements 4.1, 4.3**

### Property 5: Cohérence inter-nœuds
L'état lu par n'importe quel nœud reflète les commits validés (source de vérité unique).
**Validates: Requirements 6.3**

### Property 6: Durabilité
Une réservation confirmée survit au redémarrage de tout nœud applicatif et de la base.
**Validates: Requirements 7.3**

### Property 7: Autorisation
Seul le propriétaire d'une réservation (ou un rôle admin) peut la modifier/annuler.
**Validates: Requirements 8.3**

---

## Testing Strategy

- **Tests unitaires** : validation d'entrée, transitions d'état d'une réservation, calcul d'expiration.
- **Tests d'intégration** : parcours complet contre une vraie base PostgreSQL.
- **Test de concurrence (central)** : lancer K clients en parallèle sur le même slot, asserter qu'exactement 1 réussit → prouve Property 2. → Req 13.2
- **Test de tolérance aux pannes** : tuer un nœud API pendant une charge, vérifier continuité + absence de réservation partielle. → Req 13.3
- **Tests de sécurité** : injection SQL, jeton absent/expiré, accès à la réservation d'autrui, rate limiting. → Req 9, Req 13

## Error Handling
| Cas | Code | Comportement |
|---|---|---|
| Ressource indisponible / conflit | 409 | rollback complet, aucun état modifié |
| Entrée invalide | 400 | message explicite |
| Non authentifié | 401 | rejet |
| Non autorisé | 403 | rejet |
| Réservation introuvable | 404 | rejet |
| Trop de requêtes | 429 | rate limit |
