# Cahier de recette

Protocole de validation du système de réservation distribué (critère RNCP 1.5).
Chaque cas est rattaché à une ou plusieurs exigences et rejouable via un script.

## Prérequis

```bash
docker compose up -d --build      # démarre la stack (API sur http://localhost:8088)
```

## Environnement de validation

| Élément | Valeur |
|---|---|
| Répartiteur | Nginx sur `localhost:8088` |
| Nœuds API | 3 réplicas (stateless) |
| Base | PostgreSQL 16 (volume persistant) |
| Jeu de données | 20 chambres × 30 nuits (janvier 2026), seed automatique |

---

## C1 — Parcours nominal complet
**Exigences : 1, 2, 4.4, 5, 8**
**Script :** `infra/smoke-test.ps1`

| Étape | Attendu | Obtenu |
|---|---|---|
| Inscription + login | JWT émis, rôle CUSTOMER | ✅ token, role=CUSTOMER |
| Recherche disponibilité | liste non vide | ✅ 80 slots |
| Réservation (hold) | 201, statut HOLD, `expiresAt` renseigné | ✅ bookingId=1, HOLD |
| Confirmation | statut CONFIRMED | ✅ CONFIRMED |
| Disponibilité après réservation | slot retiré | ✅ 79 restants |
| Annulation | slot redevenu disponible | ✅ slot rendu |

**Résultat : RÉUSSI.**

---

## C2 — Absence de surréservation sous concurrence *(cas central)*
**Exigences : 3.1, 3.2, 3.3 — Property 2, Property 3**
**Script :** `infra/concurrency-test.ps1`

Protocole : 30 requêtes **réellement parallèles** (HttpClient) tentent de réserver
le **même** slot simultanément.

| Métrique | Attendu | Obtenu |
|---|---|---|
| Réservations réussies (201) | exactement 1 | ✅ 1 |
| Conflits (409) | 29 | ✅ 29 |
| Réponses inattendues | 0 | ✅ 0 |

**Résultat : RÉUSSI.** L'invariant « au plus 1 réservation active par slot » tient.

---

## C3 — Tolérance aux pannes
**Exigences : 6.4, 7.1, 7.2, 7.4**
**Script :** `infra/fault-tolerance-test.ps1`

| Étape | Attendu | Obtenu |
|---|---|---|
| Nœuds actifs initiaux | 3 | ✅ 3 |
| Arrêt forcé d'un nœud | — | `docker kill api-2` |
| Service après panne (20 req.) | ≥ 15 OK | ✅ 20/20 OK |
| Réservation après panne | HOLD créé, cohérent | ✅ HOLD |
| Réintégration après redémarrage | 3 nœuds | ✅ 3 |

**Résultat : RÉUSSI.** Aucune réservation partielle, continuité assurée.

---

## C4 — Répartition de charge multi-nœuds
**Exigences : 6.1, 6.2, 11.1**
**Commande :**
```powershell
1..6 | ForEach-Object { (Invoke-RestMethod http://localhost:8088/health).node }
```

| Attendu | Obtenu |
|---|---|
| Requêtes servies par ≥ 2 nœuds distincts | ✅ 3 nœuds distincts |

**Résultat : RÉUSSI.**

---

## C5 — Sécurité
**Exigences : 8.1, 8.2, 8.3, 9.1, 9.2, 9.3**
**Script :** `infra/security-test.ps1`

| Cas | Attendu | Obtenu |
|---|---|---|
| S1 Injection SQL (param `type`) | neutralisée, tables intactes | ✅ 80 slots intacts |
| S2 Accès sans jeton | 401 | ✅ 401 |
| S3 Jeton invalide | 401 | ✅ 401 |
| S4 Annulation par un autre utilisateur | 403 | ✅ 403 |
| S5 Rafale de requêtes | 429 déclenché | ✅ 201×429/500 |

**Résultat : RÉUSSI.** Anomalies documentées : néant (comportements conformes).
Préconisations : ajouter TLS au répartiteur et un magasin partagé (Redis) pour un
rate limiting global inter-nœuds en production.

---

## C6 — Expiration des holds
**Exigences : 4.1, 4.2, 4.3**
**Protocole manuel :**
```bash
# Démarrer avec un TTL court pour observer l'expiration
docker compose down
$env:Booking__HoldTtlSeconds=10 ; docker compose up -d
# Créer un hold, attendre > 10s + un cycle worker (30s), puis vérifier :
# le slot redevient AVAILABLE et la réservation passe à EXPIRED.
```

| Attendu | Obtenu |
|---|---|
| Hold non confirmé libéré après TTL | ✅ worker libère (log « Holds expirés libérés ») |
| Slot redevenu disponible | ✅ |

**Résultat : RÉUSSI.**

---

## C7 — Déploiement automatisé
**Exigences : 10.1, 10.2, 10.3**

| Attendu | Obtenu |
|---|---|
| `docker compose up -d --build` provisionne toute la stack | ✅ postgres + 3 API + nginx |
| Migrations + seed automatiques au démarrage | ✅ sans intervention |
| Smoke test end-to-end vert | ✅ (voir C1) |

**Résultat : RÉUSSI.**

---

## Synthèse de couverture

| Exigence | Cas de recette |
|---|---|
| 1 Consultation | C1 |
| 2 Réservation atomique | C1, C2 |
| 3 Anti-surréservation | C2 |
| 4 Expiration des holds | C1 (confirm), C6 |
| 5 Annulation | C1 |
| 6 Distribution multi-nœuds | C3, C4 |
| 7 Tolérance aux pannes | C3 |
| 8 Authentification / accès | C1, C5 |
| 9 Sécurité applicative | C5 |
| 10 Déploiement automatisé | C7 |
| 11 Observabilité | C4 (health), logs |
| 12 Documentation | README, ARCHITECTURE.md |
| 13 Cahier de recette | ce document |

**Tous les cas de recette sont au vert.**
