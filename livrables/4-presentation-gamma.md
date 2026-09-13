# Plateforme de réservation d'hôtel
## Distribuée, sans surréservation

**Architecture Logicielle — Heuristiques et Compromis**
5ESGI · Septembre 2026

---

# Le problème à résoudre

**Scénario réel : ouverture des réservations d'un congrès**

- 500 clients simultanés
- Deux clients visent **la même chambre** au même instant
- Qui obtient la chambre ? Peuvent-ils l'obtenir tous les deux ?

**Deux propriétés non négociables**

- 🔒 **Atomicité** — une réservation multi-chambres est tout ou rien
- 🚫 **Zéro surréservation** — une chambre-nuit = une seule réservation active

---

# Architecture : vue d'ensemble

```
Clients → Nginx (round-robin) → API n°1 ─┐
                               → API n°2 ─┼→ PostgreSQL 16
                               → API n°3 ─┘
```

- **Nginx** — répartiteur de charge + front statique
- **3 nœuds API** — ASP.NET Core 8, **stateless** (sans état)
- **PostgreSQL** — source de vérité unique, moteur ACID
- **Front** — HTML/CSS/JS vanilla, même origine, sans CORS

---

# Heuristique KISS — Pourquoi pas microservices ?

| Critère | Notre contexte | Verdict |
|---|---|---|
| Taille d'équipe | 3 personnes | ✅ Monolithe |
| Complexité du domaine | Simple, stable | ✅ Monolithe |
| Latence / ACID critique | Oui | ✅ Monolithe |
| Expertise systèmes distribués | Limitée | ✅ Monolithe |

**Microservices auraient imposé :**
- Transactions distribuées (pattern Saga)
- Cohérence éventuelle → risque de surréservation
- Debug distribué, infrastructure complexe

> *La solution la plus simple qui garantit les propriétés requises*

---

# Heuristique YAGNI — Périmètre maîtrisé

**✅ Implémenté**
- Comptes, JWT, ownership
- Réservation atomique (hold → confirmation → annulation)
- Expiration automatique des holds
- 3 nœuds stateless + Nginx
- Mode offline partiel (Service Worker)
- i18n FR / EN / AR avec RTL

**❌ Hors périmètre (identifié, pas préimplémenté)**
- Paiement en ligne
- Notifications email/SMS réelles
- Dashboard gestionnaire complet
- Mutations offline

> *Construire pour aujourd'hui, concevoir pour demain*

---

# Défense en profondeur — 2 barrières indépendantes

**Barrière 1 — SELECT … FOR UPDATE**
```sql
SELECT id, status FROM resource_slots
WHERE id = ANY($ids) FOR UPDATE;
```
→ Sérialise les transactions concurrentes sur un même slot
→ La 2ᵉ transaction attend, relit HELD, renvoie **409**

**Barrière 2 — Index unique partiel (garde-fou physique)**
```sql
CREATE UNIQUE INDEX uq_active_slot
  ON booking_slots (slot_id) WHERE active = TRUE;
```
→ Double réservation active **physiquement impossible**
→ Même si le code a un bug, **la base rejette**

---

# La course concurrente — séquence

| Étape | Voyageur A | Voyageur B |
|---|---|---|
| 1 | POST /bookings {slot 42} | POST /bookings {slot 42} |
| 2 | Obtient le verrou FOR UPDATE | **Bloqué, attend** |
| 3 | slot → HELD, INSERT booking | — |
| 4 | COMMIT → **201 Created** ✅ | Débloqué, relit : slot = HELD |
| 5 | — | ROLLBACK → **409 Conflict** ✅ |

---

# Compromis C1 — Cohérence forte vs Performance (QOC)

**Question :** Comment sérialiser les accès concurrents ?

| Option | Avantages | Inconvénients |
|---|---|---|
| **(a) Verrou pessimiste** ✅ | Correct, immédiat | Latence sur points chauds |
| (b) Verrou optimiste + retry | Meilleur débit si peu de conflits | Retry client, complexité |
| (c) Verrou de table | Simple | Trop large, bloque tout |

**Décision : (a)** — En pic, les conflits sont fréquents → échec immédiat (409) préférable au retry

**Prix accepté :** légère latence sur chambre très demandée
**Mitigation :** transactions courtes, index dédiés, lecture non bloquante

---

# Compromis C2 — Positionnement CAP → CP

**Théorème CAP : en cas de partition réseau, on choisit :**

| Choix | Conséquence |
|---|---|
| AP (Disponibilité) | Risque de surréservation ❌ |
| **CP (Cohérence)** ✅ | Refus propre (409/erreur) |

> *En cas de doute, refuser proprement plutôt que risquer une incohérence*

**PACELC — en fonctionnement normal :**
Else → Cohérence > Latence (EC)

---

# Compromis C3 — Atomicité vs Scalabilité

```
Nœuds API ×N  →  scalabilité illimitée
      ↓
PostgreSQL (source unique)  →  goulot d'écriture assumé
```

| Ce qu'on gagne | Ce qu'on assume |
|---|---|
| Correction garantie | Scalabilité écriture non linéaire |
| ACID natif | BD = point de coordination unique |
| Simplicité opérationnelle | — |

**Mitigations documentées :**
- Pool de connexions (NpgsqlDataSource)
- Lectures non bloquantes (pas de FOR UPDATE sur GET)
- Trajectoire : réplicas de lecture, partitionnement

---

# Heuristiques de code

**Architecture en couches**
```
Endpoints  →  Services  →  Repositories  →  PostgreSQL
```

| Heuristique | Application |
|---|---|
| **Repository Pattern** | Persistance abstraite, testable |
| **DRY** | Moteur de slots générique (resource × période) |
| **Cohésion forte** | Une classe = une responsabilité |
| **Couplage faible** | DTO records immuables en frontière |
| **Erreurs visibles** | ErrorHandlingMiddleware → codes HTTP normalisés |

---

# Modèle de données — l'invariant central

```
users ──< bookings ──< booking_slots >── resource_slots
```

**L'invariant I4 — garde-fou ultime**
```sql
CREATE UNIQUE INDEX uq_active_slot
  ON booking_slots (slot_id) WHERE active = TRUE;
```

**Machines à états**

| Entité | États |
|---|---|
| `resource_slots` | AVAILABLE → HELD → BOOKED → AVAILABLE |
| `bookings` | HOLD → CONFIRMED / EXPIRED / CANCELLED |

---

# Les autres compromis (C4 à C8)

| # | Tension | Décision | Prix |
|---|---|---|---|
| C4 | Sécurité ⇄ UX | BCrypt lent + rate limiting | Légère friction |
| C5 | Verrou pessimiste ⇄ optimiste | Pessimiste (conflits fréquents) | Latence points chauds |
| C6 | Hold gèle l'inventaire ⇄ dispo | TTL 5 min + worker auto | Inventaire gelé 5 min |
| C7 | Exactitude rate limiting ⇄ simplicité | Local par nœud | Seuil approximatif |
| C8 | Durabilité ⇄ perf écriture | Durabilité prioritaire | Latence écriture légère |

---

# Preuves mesurées

**Tests de concurrence (xUnit + PostgreSQL réel)**

```
50 clients simultanés → 1 même chambre

  ✅  1 × 201 Created   (1 réservation obtenue)
  ✅ 49 × 409 Conflict  (49 refus propres)
  ✅  0 surréservation  (vérifié en base)
```

**Scripts de recette exécutables**
- `infra/concurrency-test.sh` — 6 requêtes parallèles, même slot
- `infra/fault-tolerance-test.sh` — panne d'un nœud
- `infra/security-test.sh` — 401 / 403 / 429 / injection SQL

---

# Performance et Accessibilité RGAA 4

**Performance (Livrable 2)**

| Métrique | Résultat |
|---|---|
| Lecture p99 | **≈ 26 ms** (cible < 2 s) |
| Débit lecture | **437 req/s** (poste dev) |
| Surréservation | **0** sous 50 clients simultanés |

**Accessibilité RGAA 4 (Livrable 3)**

| Statut | Points |
|---|---|
| ✅ Conformes | Structure sémantique, contraste 8,59:1 (AAA), aria-live, labels for/id, focus visible |
| ⚠️ Restants | NC-1 room-card non focusable clavier · NC-6 erreurs non liées aux champs |

---

# Les 5 contraintes du sujet — réponses

| Contrainte | Réponse | Preuve |
|---|---|---|
| 🌐 Déploiement mutualisé → cloud | Docker + 12-factor, `docker-compose.aws.yml` | `docker compose up` en 1 commande |
| 🔐 MFA + accessible clavier | TOTP RFC 6238 (Google Authenticator), HTML sémantique | Endpoints `/api/auth/mfa/*` |
| 📶 Offline / faible bande passante | Service Worker : cache-first assets, network-first dispo (timeout 5s) | `sw.js` |
| 🌍 Localisation FR / EN / AR + RTL | `i18n.js` + JSON par langue, `dir="rtl"` auto | `frontend/locales/` |
| 📊 Analytics sans PII | Vues SQL GROUP BY, aucun user_id exposé, accès ADMIN | `AnalyticsRepository` |

---

# Risques et trajectoire

**Risques documentés**

| Risque | Mitigation |
|---|---|
| BD = point de défaillance unique | Réplication + bascule en prod |
| Rate limiting approximatif (local) | Redis partagé — ADR-005 |
| MFA non enforced au login | Intégration `AuthService.LoginAsync` — backlog |
| Contention sur chambre très demandée | Partitionnement par établissement |

**Trajectoire**
- **Court terme** — MFA complète, correctifs accessibilité NC-1/NC-6
- **Moyen terme** — Cache Redis lecture, réplicas PostgreSQL, CDN
- **Long terme** — Partitionnement, Strangler Fig si besoin microservices

---

# Récapitulatif — Heuristiques et compromis

| Heuristique | Application |
|---|---|
| **KISS** | Monolithe plutôt que microservices |
| **YAGNI** | Périmètre maîtrisé, évolutions documentées |
| **DRY** | Moteur de slots générique |
| **Couches + Repository** | Séparation claire, testabilité |
| **Défense en profondeur** | FOR UPDATE + index unique partiel |
| **CAP → CP** | Cohérence forte, refus propre |
| **QOC / ATAM** | Compromis C1-C8 analysés et documentés |
| **ADR** | 6 décisions tracées (contexte, options, raisons) |
| **Build for today, design for change** | Trajectoire planifiée, architecture extensible |

---

# Conclusion — Un système correct, prouvé, documenté

**3 messages clés**

1. 🎯 **Le problème est distribué** — résolu là où c'est maîtrisé (PostgreSQL ACID)
2. 📋 **Les compromis sont documentés** — pas de magie, des choix assumés
3. ✅ **Les preuves sont exécutables** — `docker compose up && dotnet test`

---

**Démo live**

```bash
docker compose up -d --build
# → http://localhost:8088/

dotnet test
# → 6/6 passés dont tests de concurrence réels
```
