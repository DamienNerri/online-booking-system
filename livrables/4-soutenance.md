# Livrable 4 — Support de soutenance

**Projet :** Plateforme web de réservation d'hôtel
**Module :** Architecture Logicielle — Heuristiques et Compromis (5ESGI)
**Durée cible :** ~20 min (présentation) + questions

Ce document sert de **conducteur** (plan minuté + contenu des slides + démo +
questions/réponses probables). Un jeu de slides est décrit slide par slide ; le
contenu détaillé renvoie aux livrables 1 à 3.

---

## 1. Plan minuté

| Temps | Séquence | Message clé |
|---|---|---|
| 0–2 min | Accroche + contexte | Réservation d'hôtel, pic de demande : le défi = **zéro surréservation** |
| 2–5 min | Besoins & contraintes | 6 contraintes non fonctionnelles du sujet |
| 5–7 min | Architecture | Monolithe **modulaire distribué**, justifié par heuristiques |
| 7–11 min | Le cœur : concurrence | `FOR UPDATE` + index unique partiel + ACID → **démo** |
| 11–14 min | Compromis (trade-offs) | CAP → CP, verrou pessimiste, ADR |
| 14–16 min | Performance | Mesures réelles (p99 ≈ 26 ms, 1×201/5×409) |
| 16–18 min | Accessibilité RGAA 4 | Audit partiel, conformité AA visée |
| 18–20 min | Bilan + limites + évolutions | Ce qui marche, ce qu'on améliorerait |
| Q&A | Questions | Voir § 4 |

---

## 2. Slides (contenu)

### Slide 1 — Titre
**Plateforme de réservation d'hôtel — Architecture, heuristiques et compromis.**
Équipe (3 personnes). Stack : ASP.NET Core 8 · PostgreSQL 16 · Nginx · Docker.

### Slide 2 — Le problème
> « Grand hôtel, ouverture des réservations pour un congrès. 500 clients en même
> temps. Deux cliquent sur LA MÊME chambre à la MÊME seconde. »

Défi : garantir **atomicité** et **absence de surréservation** en environnement
**distribué** et **concurrent**.

### Slide 3 — Besoins
- Fonctionnels : compte, recherche, réservation (hold→confirm), annulation,
  historique, expiration automatique.
- Non fonctionnels (sujet) : déploiement flexible, **500 users < 2 s**,
  sécurité + accessibilité, qualité dégradée, analyse métier sans PII,
  maintenabilité (équipe de 3).

### Slide 4 — Architecture (schéma C4 conteneurs)
```
Clients → Nginx (round-robin) → 3 nœuds API stateless → PostgreSQL (source de vérité)
```
Monolithe **modulaire** (Repository / Service / Endpoints), déployé en **réplicas
stateless**.

### Slide 5 — Pourquoi ce style ? (heuristiques)
Guide de sélection du cours : équipe < 10, domaine simple, latence critique, ACID
→ **monolithe**. Microservices = transactions distribuées (Saga) = complexité
injustifiée. Heuristiques : KISS, Repository, Layered, « build for today, design
for change ».

### Slide 6 — Le cœur : anti-surréservation (défense en profondeur)
1. **`SELECT … FOR UPDATE`** : sérialise les accès concurrents au même slot.
2. **Index unique partiel** `uq_active_slot (slot_id) WHERE active` : filet ultime.
3. **Transaction ACID** : tout ou rien.

```sql
BEGIN;
  SELECT ... FROM resource_slots WHERE id = ANY($ids) FOR UPDATE;  -- verrou
  -- tous AVAILABLE ? sinon ROLLBACK → 409
  INSERT INTO bookings (... status='HOLD' ...);
  UPDATE resource_slots SET status='HELD' ...;
  INSERT INTO booking_slots (... active=TRUE);   -- index unique = garde-fou
COMMIT;
```

### Slide 7 — DÉMO (voir § 3)
Course concurrente sur une même chambre → **1 succès (201), N-1 conflits (409)**.

### Slide 8 — Compromis (trade-offs)
- **CAP → CP** : en cas de doute, refuser proprement plutôt que risquer une
  incohérence.
- Cohérence ⇄ performance : verrou **fin par ligne**.
- Verrou **pessimiste** (conflits fréquents en pic).
- Décisions tracées en **ADR** (ADR-001 à 006).

### Slide 9 — Performance (mesures réelles)
- Lecture `/api/availability` sous concurrence 50 : **p50 4,8 ms / p95 19,5 ms /
  p99 25,6 ms**, ~**437 req/s** (poste de dev) → très en deçà des 2 s.
- Écriture concurrente : **1×201 / 5×409**, invariant BD respecté (1 booking actif).
- Tests automatisés : **6/6** (dont 50 clients → 1 succès).
- Optimisations priorisées : cache Redis (lecture), réplicas de lecture, CDN.

### Slide 10 — Accessibilité RGAA 4
- Audit partiel de l'écran clé (recherche + réservation).
- Base saine (HTML sémantique, `lang`, contrôles natifs, XSS-safe).
- Corrections prioritaires : **opérabilité clavier** des résultats, **focus
  visible**, régions **live** pour les messages. Cible **AA**.

### Slide 11 — Sécurité
JWT + BCrypt, contrôle d'**ownership** (pas d'IDOR), rate limiting, requêtes
**paramétrées** (anti-injection), secret JWT hors code (validation au démarrage).

### Slide 12 — Bilan, limites, évolutions
- ✅ Atomicité et anti-surréservation prouvées ; perf largement dans les clous ;
  déploiement 1 commande.
- ⚠️ Limites assumées : BD = point de contention (compromis) ; rate limiting local ;
  dashboard gestionnaire non implémenté ; offline non implémenté.
- 🔜 Évolutions : cache/réplicas, Redis pour rate limiting, Service Worker
  (BNF4), i18n (fr/en/ar), MFA.

### Slide 13 — Merci / Questions

---

## 3. Script de démonstration (à exécuter en direct)

**Pré-requis :** stack démarrée (`docker compose up -d --build`), front sur
`http://localhost:8088/`.

### 3.1 Démo fonctionnelle (front)
1. Créer un compte / se connecter.
2. Rechercher une chambre (dates futures pré-remplies).
3. Sélectionner une chambre → « Réserver » → statut **HOLD** + compte à rebours.
4. Confirmer → **CONFIRMED**. Montrer « Mes réservations ».

### 3.2 Démo concurrence (le moment fort)
Preuve automatisée (recommandé, déterministe) :
```bash
docker compose up -d --build            # stack + PostgreSQL
dotnet test --filter "StressTest_50UsersOnSameSlot_OnlyOneSucceeds"
# → 1 test réussi : 1 seul succès sur 50, 49 conflits
```
Ou preuve HTTP de bout en bout (résultat obtenu lors des mesures) :
```
6 requêtes simultanées sur le même slot → 201:1, 409:5
Vérif BD : 1 booking_slot actif, chambre HELD.
```

### 3.3 Plan B (si problème technique)
Garder une capture des résultats (tests + course 201/409) et le tableau de
métriques du Livrable 2. Expliquer le mécanisme au tableau (slide 6).

---

## 4. Questions / réponses probables

**Q — Pourquoi `FOR UPDATE` ?**
R — Pour sérialiser les transactions visant la même chambre : la seconde attend,
constate l'indisponibilité et échoue proprement (409). Pas de surréservation.

**Q — À quoi sert l'index unique partiel si vous avez déjà `FOR UPDATE` ?**
R — Défense en profondeur : c'est le garde-fou ultime. Même en cas de course
résiduelle ou de contournement du verrou, la contrainte d'unicité rend une double
réservation active physiquement impossible (violation `23505` → 409).

**Q — Pourquoi pas des microservices ?**
R — Guide de sélection : équipe de 3, domaine simple, latence critique, ACID
natif. Les microservices imposeraient des transactions distribuées (Saga) —
complexité et risque injustifiés. On garde la porte ouverte (Strangler Fig).

**Q — Comment tenez-vous les 500 utilisateurs < 2 s ?**
R — Lecture indexée et non bloquante (p99 ≈ 26 ms mesuré), nœuds stateless
scalables horizontalement, pool de connexions. Optimisations prêtes : cache
Redis en lecture, réplicas de lecture, CDN. Validation formelle via k6 en pré-prod.

**Q — Et si la base tombe ?**
R — Système **CP** : on préfère refuser une écriture plutôt que risquer une
incohérence. En prod : réplication PostgreSQL primaire/secondaire + bascule.

**Q — La sécurité ?**
R — JWT + BCrypt, ownership (pas d'IDOR), rate limiting, SQL paramétré, secret
hors code validé au démarrage. En prod : TLS au répartiteur, MFA.

**Q — L'accessibilité est-elle réelle ?**
R — Audit RGAA 4 partiel réalisé (Livrable 3). Base saine ; on a identifié et
chiffré les non-conformités (clavier, focus, messages) avec un plan d'itérations
vers AA.

**Q — Pourquoi un seul type de ressource (chambre) ?**
R — Le cœur est un moteur générique (ressource × période). Le même code gère
d'autres inventaires sans refonte : c'est un choix d'architecture (« design for
change »), pas une limite.

---

## 5. Checklist avant soutenance

- [ ] `docker compose up -d --build` OK, `http://localhost:8088/` accessible.
- [ ] `dotnet build` et `dotnet test` au vert (capture d'écran de secours).
- [ ] Dates de démo dans le futur (pré-remplies).
- [ ] `localStorage.clear()` sur le navigateur de démo (token frais).
- [ ] Slides + ce conducteur ouverts ; métriques du Livrable 2 sous la main.
- [ ] Terminal prêt pour la démo concurrence.
