# Documentation technique — Architecture

## 1. Vue d'ensemble

Le système est une architecture distribuée à nœuds applicatifs **stateless**
répartis derrière un répartiteur de charge, partageant un **stockage
transactionnel** (PostgreSQL) qui fait office de source de vérité et de point de
coordination de la concurrence.

Nginx joue un double rôle : il **sert le front statique** (HTML/CSS/JS) et
**proxifie `/api/` et `/health`** vers les nœuds applicatifs. Le front est donc
sur la même origine que l'API (pas de CORS).

```
                    ┌──────────────┐
   Clients  ─────►  │    Nginx     │  front statique + proxy API (round-robin)
                    └──────┬───────┘
              ┌────────────┼────────────┐
              ▼            ▼            ▼
        ┌─────────┐  ┌─────────┐  ┌─────────┐
        │ API n°1 │  │ API n°2 │  │ API n°3 │   ASP.NET Core, stateless
        └────┬────┘  └────┬────┘  └────┬────┘
             └───────────┼────────────┘
                         ▼
              ┌────────────────────┐
              │    PostgreSQL 16   │  ACID + SELECT ... FOR UPDATE
              └────────────────────┘
```

## 2. Composants

| Composant | Rôle | Fichiers |
|---|---|---|
| `Program.cs` | Composition (DI), pipeline, migrations au démarrage | `src/OnlineBooking.Api/Program.cs` |
| `ApiEndpoints` | Endpoints REST (auth, disponibilité, réservations) | `Endpoints/ApiEndpoints.cs` |
| `AuthService` | Inscription, login, émission JWT (BCrypt) | `Services/AuthService.cs` |
| `BookingService` | Logique métier de réservation | `Services/BookingService.cs` |
| `BookingRepository` | Accès données + transactions `FOR UPDATE` | `Repositories/BookingRepository.cs` |
| `UserRepository` | Comptes utilisateurs | `Repositories/UserRepository.cs` |
| `Migrator` | Migrations SQL versionnées, verrou consultatif | `Data/Migrator.cs` |
| `HoldExpirationWorker` | Libération périodique des holds expirés | `Workers/HoldExpirationWorker.cs` |
| `ErrorHandlingMiddleware` | Mapping exceptions → HTTP + journalisation | `Middleware/ErrorHandlingMiddleware.cs` |
| `RateLimitMiddleware` | Limitation de débit par client | `Middleware/RateLimitMiddleware.cs` |

## 3. Modèle de données

Trois tables clés (voir `migrations/001_init.sql`) :

- `resource_slots` — unité réservable atomique (ressource × période), statut
  `AVAILABLE | HELD | BOOKED`. Contrainte `UNIQUE(resource_id, period_start, period_end)`.
- `bookings` — réservation, statut `HOLD | CONFIRMED | CANCELLED | EXPIRED`,
  échéance `expires_at`.
- `booking_slots` — lien réservation ↔ slots, avec **index unique partiel**
  `uq_active_slot ON booking_slots(slot_id) WHERE active = TRUE`.

## 4. Choix de conception : atomicité et cohérence

### Pourquoi PostgreSQL comme coordinateur
Plutôt qu'un protocole de consensus maison (complexe et source d'erreurs), la
coordination des accès concurrents est déléguée aux transactions ACID de
PostgreSQL. C'est éprouvé, correct, et cohérent avec un projet pédagogique.
La dimension distribuée vient de la multiplicité des nœuds applicatifs
concurrents s'appuyant sur ce point de coordination unique.

### Chemin critique — création de réservation
```
BEGIN;
  SELECT id, status FROM resource_slots WHERE id = ANY($ids) FOR UPDATE;  -- verrou
  -- tous AVAILABLE ? sinon ROLLBACK -> 409
  INSERT INTO bookings (..., status='HOLD', expires_at=now()+ttl) RETURNING id;
  UPDATE resource_slots SET status='HELD' WHERE id = ANY($ids);
  INSERT INTO booking_slots (booking_id, slot_id, active) ...;
COMMIT;
```

Le `FOR UPDATE` verrouille les lignes visées : deux transactions concurrentes
portant sur le même slot sont **sérialisées**. La seconde attend la fin de la
première, constate que le slot n'est plus `AVAILABLE`, et échoue proprement (409).
L'index unique partiel constitue une seconde barrière : même en cas de course
résiduelle, une double insertion active sur le même `slot_id` est impossible
(violation `23505` traduite en conflit).

### Défense en profondeur (deux niveaux)
1. **Verrouillage pessimiste** (`FOR UPDATE`) — sérialise et évite le conflit.
2. **Contrainte d'unicité** (index partiel) — garantit l'invariant même si le
   verrouillage était contourné. C'est la garantie ultime (Property 2).

## 5. Propriétés de correction (rappel)

| Propriété | Garantie par | Vérifiée par |
|---|---|---|
| P1 Atomicité | transaction unique tout-ou-rien | recette C4, smoke test |
| P2 Anti-surréservation | index unique partiel + `FOR UPDATE` | `concurrency-test.ps1` |
| P3 Sérialisation | `FOR UPDATE` | `concurrency-test.ps1` |
| P4 Libération des holds | worker + requête CTE atomique | recette (expiration) |
| P5 Cohérence inter-nœuds | source de vérité unique | `fault-tolerance-test.ps1` |
| P6 Durabilité | persistance PostgreSQL (volume) | redémarrage stack |
| P7 Autorisation | contrôle d'ownership | `security-test.ps1` |

## 6. Tolérance aux pannes

- Nœuds API **stateless** : aucun état local, tout est en base. Un nœud peut
  tomber sans perte d'état.
- Nginx re-résout le DNS Docker (`resolver 127.0.0.11`) et applique
  `proxy_next_upstream` : les requêtes sont routées vers les nœuds vivants.
- Migrations protégées par un **verrou consultatif** : au démarrage simultané de
  plusieurs nœuds, un seul migre, les autres attendent puis constatent l'état à jour.

## 7. Déploiement

Entièrement automatisé via `docker-compose.yml` : build de l'image API
(multi-stage), PostgreSQL avec healthcheck, 3 réplicas API, Nginx. Les migrations
et le seed s'exécutent au démarrage de l'application. Une seule commande :
`docker compose up -d --build`.
