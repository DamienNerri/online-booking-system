# 📽️ TEMPLATE POWERPOINT : 9 Slides — Plateforme Hôtel Sans Surréservation

**Adapté au projet réel : ASP.NET Core · PostgreSQL · Nginx · Docker**

---

## **SLIDE 1 : TITRE (30 sec)**

```
┌────────────────────────────────────────────────────┐
│                                                    │
│   Plateforme de Réservation d'Hôtel :             │
│   Distribuée, Sans Surréservation                 │
│                                                    │
│   ⚡ 500 users | 🔒 ACID | 🏗️ Équipe 3           │
│                                                    │
│   [Votre nom]                                     │
│   Septembre 2026                                  │
│                                                    │
└────────────────────────────────────────────────────┘
```

**SPEAKER NOTES :**
> "Bonjour. Notre projet porte sur une plateforme de réservation
> d'hôtel distribuée. Le défi n'est pas fonctionnel — c'est
> architectural : comment garantir qu'il est physiquement
> impossible de mettre deux personnes dans la même chambre
> la même nuit, quand 500 utilisateurs réservent en même temps.
> Toute notre architecture découle de ce problème."

---

## **SLIDE 2 : LE PROBLÈME (1 min)**

```
┌────────────────────────────────────────────────────┐
│         Le Problème : la Course Critique          │
│                                                    │
│   Scénario réel :                                 │
│   Ouverture réservations congrès → 500 users      │
│                                                    │
│   Voyageur A ──────────────────────┐              │
│                        Chambre 101 │← MÊME INSTANT │
│   Voyageur B ──────────────────────┘              │
│                                                    │
│   ❌ Sans protection → SURRÉSERVATION             │
│   ✅ Notre solution → 1 gagne, 1 reçoit 409      │
│                                                    │
│   2 propriétés NON NÉGOCIABLES :                 │
│   🔒 Atomicité    — tout ou rien                  │
│   🚫 Zéro surréservation — toujours              │
│                                                    │
└────────────────────────────────────────────────────┘
```

**SPEAKER NOTES :**
> "Imaginez l'ouverture des réservations d'un grand hôtel.
> 500 personnes connectées simultanément. Deux cliquent
> sur 'Chambre 101, 15 octobre' à la même milliseconde.
> Sans protection : deux confirmations, un problème légal.
> Notre architecture rend ça physiquement impossible.
> Ce problème de concurrence structure tous nos choix."

---

## **SLIDE 3 : ARCHITECTURE (1 min 30)**

```
┌────────────────────────────────────────────────────┐
│         Architecture : Monolithe Distribué        │
│                                                    │
│              CLIENTS                              │
│                 ↓                                 │
│         ┌──────────────┐                         │
│         │    NGINX     │  Front statique         │
│         │  Round-robin │  + Proxy API            │
│         └──────────────┘                         │
│          ↙      ↓      ↘                         │
│     ┌───────┐ ┌───────┐ ┌───────┐               │
│     │ API 1 │ │ API 2 │ │ API 3 │  ASP.NET Core │
│     │       │ │       │ │       │  Stateless    │
│     └───────┘ └───────┘ └───────┘               │
│          ↘      ↓      ↙                         │
│         ┌──────────────┐                         │
│         │ PostgreSQL   │  ACID + FOR UPDATE      │
│         │   Source de  │  Source de vérité       │
│         │   vérité     │  unique                 │
│         └──────────────┘                         │
│                                                    │
│  ✓ Stateless → cloud-ready, scalable             │
│  ✓ Concurrence → gérée par PostgreSQL ACID       │
│  ✓ Équipe 3 → une seule commande : docker up     │
│                                                    │
└────────────────────────────────────────────────────┘
```

**SPEAKER NOTES :**
> "Architecture simple en 3 niveaux : Nginx distribue
> en round-robin sur 3 nœuds API identiques et sans état.
> Tous partagent une seule base PostgreSQL.
> La complexité distribuée est concentrée là où
> elle est maîtrisée : le moteur transactionnel de Postgres.
> Pas de microservices — j'y reviens."

---

## **SLIDE 4 : HEURISTIQUE KISS — POURQUOI PAS MICROSERVICES (45 sec)**

```
┌────────────────────────────────────────────────────┐
│    Heuristique KISS : Monolithe vs Microservices  │
├─────────────────────┬──────────────────────────────┤
│ Microservices       │ Monolithe Modulaire ✅       │
├─────────────────────┼──────────────────────────────┤
│ Saga (transactions  │ Transaction ACID locale      │
│ distribuées)        │ → correction garantie        │
│ Cohérence éventuelle│ Cohérence forte              │
│ → risque surréserv. │ → zéro surréservation        │
│ K8s, service mesh   │ docker compose up            │
│ Debug distribué     │ Stack trace unique           │
│ Expertise op élevée │ Stack standard               │
└─────────────────────┴──────────────────────────────┘
│                                                    │
│  Guide de sélection (cours) :                    │
│  • Équipe 3 → Monolithe                          │
│  • Domaine simple → Monolithe                    │
│  • ACID critique → Monolithe                     │
│                                                    │
│  "Systèmes distribués dans le sens qui compte :  │
│   plusieurs nœuds → un coordinateur maîtrisé"   │
│                                                    │
└────────────────────────────────────────────────────┘
```

**SPEAKER NOTES :**
> "On a sérieusement envisagé les microservices.
> Le cours nous enseigne à appliquer le guide de sélection.
> Équipe de 3, domaine stable, ACID critique : monolithe.
> Les microservices auraient imposé le pattern Saga
> pour préserver l'anti-surréservation — cohérence éventuelle,
> complexité injustifiée. KISS : la solution la plus simple
> qui garantit les propriétés requises."

---

## **SLIDE 5 : LE CŒUR — DÉFENSE EN PROFONDEUR (2 min)**

```
┌────────────────────────────────────────────────────┐
│     Défense en Profondeur : 2 Barrières           │
├────────────────────────────────────────────────────┤
│                                                    │
│  BARRIÈRE 1 — SELECT … FOR UPDATE                │
│  ┌──────────────────────────────────────────────┐ │
│  │ SELECT id, status FROM resource_slots        │ │
│  │ WHERE id = ANY($ids) FOR UPDATE;             │ │
│  └──────────────────────────────────────────────┘ │
│  → Sérialise les transactions sur un même slot   │
│  → Voyageur B attend, relit HELD, reçoit 409     │
│                                                    │
│  BARRIÈRE 2 — Index Unique Partiel               │
│  ┌──────────────────────────────────────────────┐ │
│  │ CREATE UNIQUE INDEX uq_active_slot           │ │
│  │   ON booking_slots (slot_id)                 │ │
│  │   WHERE active = TRUE;                       │ │
│  └──────────────────────────────────────────────┘ │
│  → Double réservation PHYSIQUEMENT impossible    │
│  → Même si le code a un bug, la BD rejette ✅   │
│                                                    │
│  📊 PREUVE MESURÉE :                             │
│  50 clients simultanés, 1 chambre               │
│  → 1 × 201 Created  +  49 × 409 Conflict        │
│  → 0 surréservation en base. Toujours.           │
│                                                    │
└────────────────────────────────────────────────────┘
```

**SPEAKER NOTES :**
> "Voilà le cœur. Deux barrières indépendantes.
> Première : FOR UPDATE. La transaction qui veut réserver
> verrouille les slots. Toute autre transaction concurrente
> attend. Quand elle obtient le verrou, elle relit HELD
> et renvoie un 409 propre.
> Deuxième barrière : l'index unique partiel en base.
> Même si notre code avait un bug et oubliait le FOR UPDATE,
> la base rejetterait toute double inscription active.
> C'est la défense en profondeur.
> Et ce n'est pas théorique : 50 clients simultanés,
> 1 chambre : exactement 1 succès, 49 conflits. Toujours."

---

## **SLIDE 6 : COMPROMIS CAP + TRADE-OFFS (2 min)**

```
┌────────────────────────────────────────────────────┐
│          Les Compromis Architecturaux             │
├──────────────────────┬─────────────────────────────┤
│ Choix                │ Gain        │ Prix accepté  │
├──────────────────────┼─────────────────────────────┤
│ CAP → CP             │ Zéro        │ Écriture      │
│ Cohérence prioritaire│ surréserv.  │ indispo si    │
│                      │ garantie    │ BD inacc.     │
├──────────────────────┼─────────────────────────────┤
│ Verrou pessimiste    │ Correction  │ Latence sur   │
│ (FOR UPDATE)         │ immédiate   │ chambre très  │
│                      │ pas de retry│ demandée      │
├──────────────────────┼─────────────────────────────┤
│ PostgreSQL unique    │ ACID natif  │ Scalabilité   │
│ (pas Redis/Redlock)  │ Simplicité  │ écriture non  │
│                      │0 dépendance │ linéaire      │
├──────────────────────┼─────────────────────────────┤
│ Rate limiting local  │ Simple      │ Seuil global  │
│ (pas Redis partagé)  │ 0 infra     │ approximatif  │
│                      │ suppl.      │ → Redis V2    │
├──────────────────────┼─────────────────────────────┤
│ Hold TTL 5 min       │ Inventaire  │ Chambre gelée │
│ + worker auto        │ libéré auto │ 5 min max     │
└──────────────────────┴─────────────────────────────┘
│                                                    │
│  "En cas de doute, refuser proprement plutôt      │
│   que risquer une incohérence" — principe CAP→CP  │
│                                                    │
└────────────────────────────────────────────────────┘
```

**SPEAKER NOTES :**
> "Tous ces choix sont des compromis documentés — méthode
> QOC et ATAM du cours.
> Le plus structurant : CAP → CP. En cas de partition réseau,
> on choisit la cohérence. On préfère refuser une réservation
> plutôt que risquer une surréservation.
> Le verrou pessimiste : en pic de réservations, les conflits
> sont fréquents — l'échec immédiat en 409 est plus honnête
> qu'un retry silencieux côté client.
> Chaque compromis est assumé, documenté dans nos 6 ADR."

---

## **SLIDE 7 : CONTRAINTES DU SUJET (1 min 30)**

```
┌────────────────────────────────────────────────────┐
│     6 Contraintes du Sujet → Réponses Réelles     │
├──────────────────┬─────────────────────────────────┤
│ Contrainte       │ Réponse architecturale          │
├──────────────────┼─────────────────────────────────┤
│ 🌐 Déploiement   │ Docker + 12-factor              │
│ mutualisé→cloud  │ docker-compose.aws.yml fourni   │
├──────────────────┼─────────────────────────────────┤
│ ⚡ 500 users <2s │ 3 nœuds stateless + Nginx       │
│                  │ p99 lecture = 26 ms ✅           │
├──────────────────┼─────────────────────────────────┤
│ 🔐 MFA + clavier │ TOTP RFC 6238 (Authenticator)   │
│                  │ Front HTML sémantique natif     │
├──────────────────┼─────────────────────────────────┤
│ 📶 Offline/rural │ Service Worker : cache-first    │
│                  │ assets + fallback disponibilités│
├──────────────────┼─────────────────────────────────┤
│ 🌍 FR/EN/AR +RTL │ i18n.js + JSON + dir="rtl" auto │
├──────────────────┼─────────────────────────────────┤
│ 📊 Stats sans PII│ Vues SQL GROUP BY, 0 user_id    │
│                  │ exposé, accès ADMIN seul        │
└──────────────────┴─────────────────────────────────┘
```

**SPEAKER NOTES :**
> "Le sujet posait 6 contraintes transverses.
> Chacune a une réponse concrète avec une preuve.
> Sur le déploiement : docker compose up en une commande,
> aucune dépendance propriétaire.
> Sur la performance : p99 à 26 ms en lecture — 75 fois
> sous la cible de 2 secondes.
> Sur l'offline : le Service Worker applique cache-first
> pour les assets — la page s'ouvre sans réseau.
> Sur l'arabe : dir=rtl sur html, le CSS s'adapte
> automatiquement, sans surcharge de règles."

---

## **SLIDE 8 : VALIDATION — PREUVES (1 min 30)**

```
┌────────────────────────────────────────────────────┐
│         Validation : Les Preuves Parlent          │
├────────────────────────────────────────────────────┤
│                                                    │
│  ⚡ PERFORMANCE (Livrable 2)                      │
│                                                    │
│     Lecture p99  :   26 ms   ✅ (cible < 2 000ms) │
│     Débit        :  437 req/s  (poste dev)        │
│     Surréservation :   0       ✅ (toujours)      │
│                                                    │
│  🧪 TESTS AUTOMATISÉS (xUnit + PostgreSQL réel)  │
│                                                    │
│     ✅ 2 users même slot    → 1 succès, 1 conflit │
│     ✅ 50 users même chambre → 1 succès, 49 conflits│
│     ✅ Index unique partiel  → violation 23505    │
│     ✅ 3 users, 3 slots diff → 3 succès           │
│     6/6 tests passés                              │
│                                                    │
│  ♿ ACCESSIBILITÉ RGAA 4 (Livrable 3)            │
│                                                    │
│     Audit WAVE   : 0 erreur, contraste 8.59:1 AAA │
│     NC corrigées : focus visible, aria-live,      │
│                    labels for/id                  │
│     Restantes    : NC-1 clavier, NC-6 erreurs     │
│                                                    │
└────────────────────────────────────────────────────┘
```

**SPEAKER NOTES :**
> "On ne déclare pas que le système est correct — on le prouve.
> Les tests de concurrence tournent sur une vraie base
> PostgreSQL, pas des mocks. 50 clients simultanés,
> une seule chambre : exactement 1 succès, 49 conflits propres.
> En lecture, p99 à 26 ms — très en deçà de la cible 2 secondes.
> L'audit WAVE confirme 0 erreur automatique,
> contraste AAA validé.
> Tout est exécutable : docker compose up, dotnet test."

---

## **SLIDE 9 : CONCLUSION (1 min)**

```
┌────────────────────────────────────────────────────┐
│                   Conclusion                      │
├────────────────────────────────────────────────────┤
│                                                    │
│  ✅ Objectifs atteints :                          │
│                                                    │
│     ✓ Zéro surréservation — prouvé en tests      │
│     ✓ 6 contraintes adressées (6/6)              │
│     ✓ Heuristiques KISS · YAGNI · DRY appliquées │
│     ✓ Compromis C1–C8 documentés (ATAM/QOC)      │
│     ✓ 6 ADR tracés (contexte, options, décision)  │
│     ✓ Performance : p99 26ms < 2s ✅             │
│     ✓ Équipe 3 → docker compose up               │
│                                                    │
│  🛣️  Trajectoire future :                         │
│     • Court terme  : MFA enforced au login        │
│     • Moyen terme  : Cache Redis, réplicas lecture│
│     • Long terme   : Partitionnement, Strangler   │
│                      Fig si microservices justifiés│
│                                                    │
│  📌 Principe directeur :                          │
│     "En cas de doute, refuser proprement         │
│      plutôt que risquer une incohérence."        │
│                                                    │
│  ❓ QUESTIONS ?                                   │
│                                                    │
└────────────────────────────────────────────────────┘
```

**SPEAKER NOTES :**
> "En résumé : on a appliqué les heuristiques du cours
> pour justifier chaque décision.
> L'invariant anti-surréservation est prouvé — pas déclaré.
> Les 6 compromis sont documentés dans les ADR.
> La trajectoire est planifiée.
> C'est ça l'architecture logicielle : pas de magie,
> des choix assumés, documentés et prouvés.
> Questions ?"

---

## **SLIDE BONUS : SÉQUENCE DE LA COURSE CONCURRENTE**

```
┌────────────────────────────────────────────────────┐
│    Ce qui se passe au niveau milliseconde         │
├──────────┬─────────────────────┬───────────────────┤
│ Étape    │ Voyageur A          │ Voyageur B        │
├──────────┼─────────────────────┼───────────────────┤
│ 1        │ POST /bookings      │ POST /bookings    │
│          │ {slot 42}           │ {slot 42}         │
├──────────┼─────────────────────┼───────────────────┤
│ 2        │ ✅ Obtient le       │ 🔒 Bloqué,        │
│          │ verrou FOR UPDATE   │ attend...         │
├──────────┼─────────────────────┼───────────────────┤
│ 3        │ slot → HELD         │ —                 │
│          │ INSERT booking      │                   │
├──────────┼─────────────────────┼───────────────────┤
│ 4        │ COMMIT              │ Débloqué,         │
│          │ → 201 Created ✅    │ relit : HELD      │
├──────────┼─────────────────────┼───────────────────┤
│ 5        │ —                   │ ROLLBACK          │
│          │                     │ → 409 Conflict ✅ │
└──────────┴─────────────────────┴───────────────────┘
│                                                    │
│  Résultat : 1 réservation active en base.         │
│  Toujours. Sans exception.                        │
│                                                    │
└────────────────────────────────────────────────────┘
```

**SPEAKER NOTES :**
> "Ce tableau montre exactement ce qui se passe.
> A et B arrivent en même temps. A obtient le verrou.
> B est bloqué au niveau de la base de données.
> A commit, B est débloqué, relit le statut HELD,
> et reçoit un 409 propre. Pas de retry, pas d'ambiguïté.
> Une réservation active. Toujours."

---

## 🎯 RECAP TIMING

| Slide | Durée | Contenu |
|---|---|---|
| 1. Titre | 0:30 | Accroche |
| 2. Le problème | 1:00 | La course critique |
| 3. Architecture | 1:30 | Monolithe distribué |
| 4. KISS | 0:45 | Pourquoi pas microservices |
| 5. Défense en profondeur | 2:00 | FOR UPDATE + index unique |
| 6. Compromis | 2:00 | CAP, QOC, trade-offs |
| 7. Contraintes | 1:30 | 6 contraintes → réponses |
| 8. Validation | 1:30 | Tests + perf + RGAA |
| 9. Conclusion | 1:00 | Synthèse + questions |
| **TOTAL** | **11:45** | + BONUS + questions = 20 min |

---

## ❓ QUESTIONS JURY PROBABLES

| Question | Réponse clé |
|---|---|
| Pourquoi pas Redis pour le verrou ? | PostgreSQL déjà là, ACID natif, 0 dépendance (ADR-001) |
| Que se passe-t-il si la BD tombe ? | CP assumé : écriture impossible, réplication en prod |
| Comment tu prouves les 500 users ? | Livrable 2 + 50 clients xUnit sur PostgreSQL réel |
| Pourquoi WHERE active = TRUE ? | Permet lignes inactives (annulées) sur même slot, une seule active |
| La MFA est vraiment implémentée ? | TOTP RFC 6238, endpoints setup/verify, pas encore enforced au login — dette documentée |
| Et l'offline ? | Service Worker : cache-first assets, network-first dispo timeout 5s |
| Et l'arabe RTL ? | dir="rtl" sur html, CSS s'adapte auto, locales/ar.json |
| Si attaquant fait 100 req/s ? | Rate limiting local → approximatif, Redis partagé en backlog ADR-005 |
| Comment scaler à 10k req/s ? | Partitionnement, réplicas lecture — trajectoire §14 documentée |
