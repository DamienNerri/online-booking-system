# Livrable 2 — Rapport d'analyse de performance

**Projet :** Plateforme web de réservation d'hôtel
**Module :** Architecture Logicielle — Heuristiques et Compromis (5ESGI)

Ce rapport applique la démarche structurée du cours : **baseline → objectifs →
test → optimiser → valider → monitorer**. Il analyse les scénarios critiques
(pics de charge, latence, I/O), présente des **mesures réelles** effectuées sur la
stack, et propose des optimisations priorisées.

---

## 1. Objectifs quantifiés (SLA)

Issu de la contrainte de performance du sujet :

| Métrique | Cible |
|---|---|
| Utilisateurs simultanés en pic | 500 |
| Temps de réponse | < 2 s |
| Taux d'erreur (hors 409/429 métier) | ≈ 0 % |
| Invariant anti-surréservation | 100 % (aucune double réservation) |

---

## 2. Environnement de test

- **Stack :** Docker Compose — 3 nœuds API ASP.NET Core 8 (stateless) derrière
  Nginx (round-robin), PostgreSQL 16, sur poste de développement (macOS).
- **Jeu de données :** ~7 300 slots (20 chambres × 365 nuits, horizon glissant).
- **Outillage :** mesures HTTP via `curl` (latence par requête) et script Python
  multithread (course concurrente synchronisée par barrière).
- **Représentativité :** environnement mono-machine → les valeurs absolues sont
  optimistes (pas de latence réseau réelle), mais les **rapports** (p50/p95/p99,
  comportement sous concurrence) et surtout la **correction** sont significatifs.

> Note méthodologique (cf. cours) : un test de performance idéal se fait sur un
> environnement représentatif de la production avec des données réalistes. Les
> chiffres ci-dessous constituent une **baseline** de développement ; le § 6
> décrit le protocole de montée en charge vers 500 utilisateurs.

---

## 3. Scénarios critiques

### Scénario A — Consultation de disponibilités sous charge (lecture)
Le chemin le plus fréquent : recherche de chambres. Non transactionnel, donc
scalable horizontalement.

**Mesure — latence séquentielle (20 requêtes) :**

| min | médiane | max |
|---|---|---|
| 2,0 ms | 2,5 ms | 19,1 ms |

**Mesure — charge concurrente (300 requêtes, concurrence 50) :**

| Débit | p50 | p95 | p99 | max |
|---|---|---|---|---|
| **437 req/s** | 4,8 ms | 19,5 ms | 25,6 ms | 44,1 ms |

**Interprétation :** p99 à ~26 ms, soit **~75× sous** le seuil de 2 s. La lecture
est indexée (`ix_slots_search` sur `resource_type, status, period_start,
period_end`) et non bloquante.

### Scénario B — Pic de demande sur une même chambre (écriture, concurrence critique)
Le vrai défi métier : N clients visent **le même slot** au même instant.

**Mesure — course concurrente (6 requêtes simultanées, même slot, départ
synchronisé par barrière) :**

| Réponse | Nombre |
|---|---|
| `201 Created` (réservation obtenue) | **1** |
| `409 Conflict` (slot déjà pris) | **5** |

**Vérification base de données :** `booking_slots` actifs pour ce slot = **1**,
statut de la chambre = **HELD**.

```
slot_id | actifs | statut
--------+--------+-------
   201  |   1    | HELD
```

**Interprétation :** aucune surréservation possible, **prouvé de bout en bout**
(HTTP → Nginx → API → PostgreSQL), pas seulement en test unitaire. Le premier
gagne, les autres échouent proprement (409). C'est la démonstration à faire au
jury.

### Scénario C — Preuve d'atomicité et de sérialisation (tests automatisés)
La suite `ConcurrencyTests` (xUnit, PostgreSQL réel) valide 4 propriétés :

| Test | Vérifie | Résultat |
|---|---|---|
| `TwoUsersCannotBookSameSlot_OneSucceedsOneFails` | anti-surréservation à 2 | ✅ |
| `ThreeUsersBookThreeDifferentSlots_AllSucceed` | pas de faux conflit | ✅ |
| `StressTest_50UsersOnSameSlot_OnlyOneSucceeds` | 50 clients, 1 slot → 1 succès | ✅ |
| `UniqueIndexPreventsDoubleActiveBookingSlots` | filet index unique partiel | ✅ |

Exécution : `dotnet test` → **6 réussis / 0 échec** (4 concurrence + 2 smoke).

### Scénario D — I/O et durabilité
Les écritures (HOLD/CONFIRM) impliquent un commit durable (volume PostgreSQL).
C'est le compromis C5 (durabilité > latence d'écriture) : acceptable car une
réservation confirmée est un engagement.

---

## 4. Analyse des goulots d'étranglement

| Goulot potentiel (cf. cours) | Présent ici ? | Mitigation en place / cible |
|---|---|---|
| Requêtes SQL non optimisées | Non | Index dédiés, requêtes paramétrées, pas de N+1 |
| Requêtes N+1 | Non | Agrégation SQL (`array_agg` pour les slots d'une réservation) |
| Base de données saturée (contention écriture) | **Oui, par conception** | Verrou **fin par ligne**, transactions courtes ; cible : partitionnement / réplicas de lecture |
| Serveur applicatif sous-dimensionné | Non | Nœuds stateless → ajout de réplicas trivial |
| Gestion mémoire / GC | Non observé | Charge de dev faible |
| Réseau congestionné | N/A (mono-machine) | À mesurer en environnement distribué |

**Point de contention identifié et assumé :** PostgreSQL est le coordinateur de
concurrence (compromis C3 du Livrable 1). C'est le prix de la cohérence forte et
de l'anti-surréservation.

---

## 5. Recommandations d'optimisation (priorisées)

Priorisation par impact utilisateur / effort (cf. cours) :

| Priorité | Optimisation | Type | Gain attendu |
|---|---|---|---|
| 1 | **Cache lecture** (Redis) sur `/api/availability` | Backend | Décharge la BD sur le chemin le plus fréquent ; débit ↑ |
| 2 | **Réplicas de lecture** PostgreSQL (séparation lecture/écriture) | Architecture | Scalabilité lecture, BD d'écriture soulagée |
| 3 | **CDN + compression HTTP** pour le front statique | Frontend | Temps de chargement initial ↓ en connectivité faible (BNF4) |
| 4 | **Rate limiting partagé** (Redis) au lieu de local par nœud | Backend | Plafonnement exact derrière le round-robin (ADR-005) |
| 5 | **Partitionnement** par établissement/région (à grande échelle) | Données | Réduit la contention écriture, scalabilité horizontale |
| 6 | **Auto-scaling** des nœuds API selon la charge | Infra | Élasticité aux pics (congrès, saison) |
| 7 | **Observabilité** : APM, logs centralisés, alerting sur p95/erreurs | Monitoring | Détection proactive des régressions |

**Techniques du cours mobilisables :** cache-aside (Redis), connection pooling
(déjà en place via `NpgsqlDataSource`), lazy loading / pagination des résultats,
`EXPLAIN` pour valider les plans de requête.

---

## 6. Protocole de validation vers 500 utilisateurs (à exécuter en pré-prod)

Pour valider formellement la contrainte « 500 utilisateurs simultanés < 2 s »,
la démarche recommandée (outils du cours : **k6 / Gatling / JMeter**) :

1. **Baseline** : mesurer l'état actuel (fait, § 3).
2. **Montée progressive** : 50 → 100 → 250 → 500 utilisateurs virtuels, paliers
   de stabilisation.
3. **Mix réaliste** : 80 % lecture (recherche) / 20 % écriture (réservation),
   incluant des conflits sur des slots populaires.
4. **Métriques** : p50/p95/p99 de latence, débit (req/s), taux d'erreur, et
   corrélation avec CPU / mémoire / connexions BD.
5. **Critères de succès** : p95 < 2 s, 0 surréservation, taux d'erreur (hors
   409/429 métier) ≈ 0 %.

Exemple de script k6 (esquisse) :

```javascript
import http from 'k6/http';
import { check } from 'k6';
export const options = { stages: [
  { duration: '30s', target: 100 },
  { duration: '1m',  target: 500 },
  { duration: '30s', target: 0 },
]};
export default function () {
  const res = http.get('http://HOST:8088/api/availability?type=HOTEL_ROOM&from=2026-09-20&to=2026-09-22');
  check(res, { 'status 200': (r) => r.status === 200, 'p<2s': (r) => r.timings.duration < 2000 });
}
```

---

## 7. Synthèse

- **Latence de lecture** : p99 ≈ 26 ms sous concurrence 50, très en deçà des 2 s.
- **Débit de lecture** mesuré : ~437 req/s sur poste de dev, scalable via réplicas.
- **Correction sous concurrence** : prouvée de bout en bout (1×201 / 5×409) et par
  la suite de tests automatisés (50 clients → 1 succès).
- **Compromis assumé** : contention d'écriture concentrée sur PostgreSQL, au
  bénéfice d'une cohérence forte (zéro surréservation).
- **Prochaines étapes** priorisées : cache lecture, réplicas de lecture, CDN,
  rate limiting partagé, puis partitionnement et auto-scaling pour l'échelle.

La boucle **test → mesure → amélioration** est en place ; les optimisations
sont documentées et priorisées, prêtes à être validées par un test de charge
formel en pré-production.
