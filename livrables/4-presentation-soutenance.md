# Livrable 4 — Présentation de soutenance

**Projet :** Plateforme web de réservation d'hôtel  
**Module :** Architecture Logicielle — Heuristiques et Compromis (5ESGI)  
**Durée estimée :** 25-30 minutes de présentation + 5-10 minutes de questions  
**Format conseillé :** 1 slide par section (19 slides au total)

---

> Ce document décrit précisément le contenu et le discours de chaque slide.
> Il est conçu pour être transformé directement en PowerPoint / Google Slides / Keynote.
> Un fichier optimisé pour Gamma AI est disponible : `4-presentation-gamma.md`

---

## SLIDE 1 — Titre

**Titre principal :**  
Plateforme de réservation d'hôtel — distribuée, sans surréservation

**Sous-titre :**  
Architecture logicielle : heuristiques et compromis — 5ESGI

**Informations :**  
Nom(s) de l'équipe · Septembre 2026

**Ce qu'on dit à l'oral :**  
> « Bonjour, notre projet porte sur la conception et l'implémentation d'une plateforme
> de réservation d'hôtel. Ce qui nous intéressait n'était pas de faire le plus de
> fonctionnalités possible, mais de résoudre un problème d'architecture très concret :
> comment garantir qu'il ne peut jamais y avoir deux personnes sur la même chambre
> la même nuit, dans un système distribué avec des centaines d'utilisateurs simultanés.
> Toute notre architecture découle de ce problème. »

---

## SLIDE 2 — Le problème à résoudre (accroche)

**Titre :** Un problème architectural, pas un problème fonctionnel

**Visuel suggéré :** deux flèches qui pointent vers la même chambre en même temps

**Contenu :**

> Scénario réel : ouverture des réservations pour un congrès  
> → 500 clients simultanés  
> → Deux clients visent **la même chambre la même nuit** au même instant

**Deux propriétés non négociables :**
- **Atomicité** — une réservation multi-chambres est tout ou rien
- **Zéro surréservation** — une chambre-nuit = une seule réservation active

**Ce qu'on dit à l'oral :**  
> « Imaginez l'ouverture des réservations d'un grand hôtel pour un congrès.
> 500 personnes se connectent en même temps. Deux d'entre elles cliquent sur
> "Réserver la chambre 101 pour le 15 octobre" à la même milliseconde.
> Qui obtient la chambre ? Est-ce qu'elles peuvent toutes les deux l'obtenir ?
> Si oui, vous avez une surréservation — c'est une faute grave, un problème légal,
> une mauvaise expérience client. Notre architecture doit rendre ça physiquement impossible.
> C'est de ça qu'on va parler aujourd'hui. »

---

## SLIDE 3 — Vue d'ensemble de l'architecture

**Titre :** Architecture : monolithe modulaire distribué

**Schéma central :**

```
Clients → Nginx (round-robin) → API n°1 ┐
                               → API n°2 ├→ PostgreSQL 16
                               → API n°3 ┘
```

**Légende :**
- **Nginx** : répartiteur de charge + serveur du front statique
- **3 nœuds API** : ASP.NET Core 8, **sans état** (stateless)
- **PostgreSQL** : source de vérité unique, moteur ACID, coordinateur de concurrence
- **Front** : HTML/CSS/JS vanilla, servi sur la même origine (pas de CORS)

**Ce qu'on dit à l'oral :**  
> « L'architecture est simple à expliquer : des clients arrivent sur Nginx qui
> les distribue en round-robin sur 3 nœuds API identiques et sans état.
> Ces 3 nœuds partagent une seule base PostgreSQL. C'est volontairement simple.
> Le secret est dans comment on utilise cette base pour garantir les propriétés
> qu'on vient de citer. On reviendra sur ce choix et pourquoi on a écarté
> les microservices. »

---

## SLIDE 4 — Heuristique : KISS et pourquoi pas microservices

**Titre :** Heuristique KISS — « La solution la plus simple qui fonctionne »

**Tableau comparatif :**

| Microservices | Monolithe modulaire distribué |
|---|---|
| Transactions distribuées (Saga) | Transaction ACID locale |
| Cohérence éventuelle | Cohérence forte |
| Infrastructure complexe | `docker compose up` |
| Debug distribué | Stack trace unique |
| Expertise requise élevée | Stack standard |

**Question clé (guide de sélection du cours) :**

| Critère | Notre contexte | Verdict |
|---|---|---|
| Taille d'équipe | 3 personnes | Monolithe |
| Complexité du domaine | Simple, stable | Monolithe |
| Latence / ACID critique | Oui | Monolithe |
| Expertise systèmes distribués | Limitée | Monolithe |

**Ce qu'on dit à l'oral :**  
> « On a sérieusement envisagé les microservices. Mais le cours nous enseigne
> à appliquer le guide de sélection avant de choisir un style architectural.
> Équipe de 3, domaine stable, besoin ACID critique : le monolithe s'impose.
> Les microservices auraient imposé des transactions distribuées — le pattern Saga —
> pour préserver l'anti-surréservation. C'est une complexité énorme, injustifiée
> pour notre contexte. On applique KISS : la solution la plus simple qui garantit
> les propriétés requises. »
>
> « Mais on reste distribué dans le sens qui compte : plusieurs nœuds concurrents
> convergent vers un point de coordination unique — la base. C'est là que la
> complexité distribuée est concentrée, là où elle est maîtrisée. »

---

## SLIDE 5 — Heuristique : Build for today, design for change (YAGNI)

**Titre :** Heuristique YAGNI — Construire pour aujourd'hui, concevoir pour demain

**Dans le périmètre (implémenté) :**
- Comptes, JWT, ownership
- Réservation (hold → confirmation), annulation
- Expiration automatique des holds
- Déploiement multi-nœuds conteneurisé
- **Mode offline partiel** (Service Worker : cache-first assets, network-first disponibilités)
- **i18n FR / EN / AR avec RTL** (fichiers JSON + `i18n.js`)

**Hors périmètre (identifié, pas préimplémenté) :**
- Paiement en ligne
- Notifications email/SMS réelles
- Dashboard gestionnaire complet
- Mode offline complet (mutations offline)
- Localisation complète (formats date/monnaie)

**Ce qu'on dit à l'oral :**  
> « On a fait un choix assumé : on ne préimplémente pas ce dont on n'a pas besoin
> aujourd'hui. Aucun code de paiement, pas de notifications réelles.
> Ce n'est pas de la paresse — c'est l'application de l'heuristique YAGNI.
> Mais ces évolutions sont identifiées et documentées dans le dossier.
> L'architecture est conçue pour les accueillir : le modèle de données est générique
> (resource × période), ce qui permet de réutiliser le même socle pour d'autres
> types de ressources sans refonte. »

---

## SLIDE 6 — Le cœur du sujet : la stratégie anti-surréservation

**Titre :** Défense en profondeur — 2 barrières indépendantes

**Barrière 1 — `SELECT … FOR UPDATE`**
```sql
SELECT id, status FROM resource_slots
WHERE id = ANY($ids) FOR UPDATE;
```
→ Sérialise les transactions concurrentes sur un même slot  
→ La 2ᵉ transaction attend, relit le statut HELD, échoue proprement (409)

**Barrière 2 — Index unique partiel (garde-fou ultime)**
```sql
CREATE UNIQUE INDEX uq_active_slot
  ON booking_slots (slot_id) WHERE active = TRUE;
```
→ Rend une double réservation active **physiquement impossible**  
→ Même si le code applicatif avait un bug, la base rejette

**Résultat mesuré :**  
6 requêtes concurrentes sur le même slot → **1× 201** + **5× 409**  
Tests automatisés : 50 clients sur 1 chambre → **1 succès, 49 conflits**

**Ce qu'on dit à l'oral :**  
> « Voilà le cœur de notre architecture. Deux barrières indépendantes.
> La première : quand une transaction veut réserver un slot, elle pose un verrou
> dessus via FOR UPDATE. Toute autre transaction qui veut le même slot doit attendre.
> Quand elle obtient enfin le verrou, elle relit le statut — qui est maintenant
> HELD — et renvoie un 409 propre.
> La deuxième barrière : l'index unique partiel en base. Même si notre code
> avait un bug et oubliait le FOR UPDATE, la base rejetterait toute double
> inscription active. C'est la défense en profondeur. »

---

## SLIDE 7 — Diagramme de séquence : la course concurrente

**Titre :** Ce qui se passe exactement quand deux clients visent le même slot

| Étape | Voyageur A | Voyageur B |
|---|---|---|
| 1 | POST /bookings {slot 42} | POST /bookings {slot 42} |
| 2 | Obtient le verrou FOR UPDATE | **Bloqué, attend** |
| 3 | slot → HELD, INSERT booking | — |
| 4 | COMMIT → **201 Created** ✅ | Débloqué, relit : slot = HELD |
| 5 | — | ROLLBACK → **409 Conflict** ✅ |

**Ce qu'on dit à l'oral :**  
> « Ce diagramme montre exactement ce qui se passe. A et B arrivent en même temps.
> A obtient le verrou sur le slot 42. B est bloqué. A complète sa transaction,
> commit. B est débloqué, relit le slot — il est HELD — et renvoie immédiatement
> un 409 au voyageur B. Propre, correct, sans surréservation. »

---

## SLIDE 8 — Compromis C1 : Cohérence forte ⇄ Performance

**Titre :** Compromis C1 — Cohérence forte vs Performance (analyse QOC)

**Question :** Comment sérialiser les accès concurrents à un même slot ?

| Option | Avantages | Inconvénients |
|---|---|---|
| **(a) Verrou pessimiste (FOR UPDATE)** ✅ | Correct, simple, immédiat | Latence sur points chauds |
| (b) Verrou optimiste + retry | Meilleur débit si peu de conflits | Retry côté client, complexité |
| (c) Verrou de table | Simple | Trop large, bloque tout |

**Décision : Option (a)**  
En pic de réservations, les conflits sont fréquents → l'échec immédiat (409)  
est plus honnête qu'un retry silencieux.

**Prix accepté :** latence accrue sur une chambre très demandée  
**Mitigation :** transactions courtes, index adaptés, lecture non bloquante

**Ce qu'on dit à l'oral :**  
> « On a analysé les options avec la méthode QOC du cours. Trois options de verrouillage.
> On a choisi le verrou pessimiste parce que dans notre contexte — pic de réservations —
> les conflits sont fréquents. Si on choisissait le verrou optimiste, on aurait des
> retries côté client, une logique plus complexe, et un meilleur débit seulement si
> les conflits sont rares. Ce n'est pas notre cas.
> Le prix qu'on accepte : légère latence sur une chambre très populaire. C'est assumé
> et documenté. »

---

## SLIDE 9 — Compromis C2 : Positionnement CAP → CP

**Titre :** Compromis C2 — Théorème CAP : on choisit la Cohérence

| En cas de partitionnement réseau... | Notre choix |
|---|---|
| Priorité disponibilité (AP) | Risque de surréservation ❌ |
| Priorité cohérence **(CP)** | **Refus propre (409/erreur)** ✅ |

**Principe directeur :**  
> *« En cas de doute, refuser proprement plutôt que risquer une incohérence. »*

**PACELC (en fonctionnement normal) :**  
Else → on privilégie la **Cohérence** sur la **Latence** (EC)

**Ce qu'on dit à l'oral :**  
> « Le théorème CAP dit qu'en cas de partition réseau, on ne peut pas avoir à la
> fois cohérence et disponibilité. On a choisi la cohérence.
> Pourquoi ? Parce qu'une surréservation est inacceptable — c'est un problème légal
> et métier grave. Un refus propre, c'est certes décevant pour l'utilisateur, mais
> c'est gérable. En fonctionnement normal, on applique aussi PACELC : on préfère
> prendre un peu plus de latence pour garantir la cohérence. »

---

## SLIDE 10 — Compromis C3 : Atomicité ⇄ Scalabilité horizontale

**Titre :** Compromis C3 — Atomicité vs Scalabilité (le goulot assumé)

```
Nœuds API ×N → scalabilité illimitée
      ↓
PostgreSQL (source de vérité unique) → goulot d'écriture assumé
```

| Ce qu'on gagne | Ce qu'on sacrifie partiellement |
|---|---|
| Correction garantie | Scalabilité écriture non linéaire |
| Simplicité opérationnelle | — |
| ACID natif | — |

**Mitigations documentées :**
- Pool de connexions (NpgsqlDataSource)
- Lectures non bloquantes (pas de FOR UPDATE sur GET)
- Trajectoire : partitionnement, réplicas de lecture

**Ce qu'on dit à l'oral :**  
> « Notre principal compromis structurel : les nœuds API peuvent scaler à l'infini,
> mais ils convergent tous vers une seule base. C'est un goulot potentiel en écriture.
> On l'assume. Pour notre contexte — hôtel, pics ponctuels — c'est totalement acceptable.
> Et on a prévu la trajectoire si ça doit évoluer. »

---

## SLIDE 11 — Heuristique : Repository Pattern + couches + DRY

**Titre :** Heuristiques de code : cohésion, couplage, DRY

**Architecture en couches :**

```
Endpoints → Services → Repositories → PostgreSQL
(contrats)  (métier)   (persistance)
```

| Heuristique | Application |
|---|---|
| **DRY** | Moteur de slots générique (resource × période) |
| **Cohésion forte** | Chaque classe a une responsabilité unique |
| **Couplage faible** | Couches communiquent via DTO records immuables |
| **Repository Pattern** | Persistance abstraite → testable, remplaçable |
| **Erreurs visibles** | `ErrorHandlingMiddleware` → codes HTTP normalisés |

**Ce qu'on dit à l'oral :**  
> « Côté code, on a appliqué les heuristiques classiques du cours.
> Architecture en couches avec dépendances dirigées vers le bas uniquement.
> Repository Pattern pour abstraire la persistance.
> DRY : le moteur de réservation est générique, resource × période.
> On pourrait réserver des salles de conférence ou des vélos avec le même code. »

---

## SLIDE 12 — Modèle de données et l'invariant central

**Titre :** Le schéma de données : où vit l'invariant anti-surréservation

```
users ──< bookings ──< booking_slots >── resource_slots
```

**L'invariant I4 (central) :**
```sql
CREATE UNIQUE INDEX uq_active_slot
  ON booking_slots (slot_id) WHERE active = TRUE;
```
→ Un slot = au plus une réservation active — **physiquement impossible d'en avoir deux**

**Machines à états :**

| Entité | Transitions |
|---|---|
| `resource_slots` | AVAILABLE → HELD → BOOKED → AVAILABLE |
| `bookings` | HOLD → CONFIRMED / EXPIRED / CANCELLED |

**Ce qu'on dit à l'oral :**  
> « L'invariant I4 — l'index unique partiel — vit directement dans la base de données.
> Ce n'est pas une règle dans le code, c'est une contrainte physique.
> Même si quelqu'un déployait une nouvelle version de l'API avec un bug,
> la base refuserait la double réservation. »

---

## SLIDE 13 — Les autres compromis (C4 à C8)

**Titre :** Les autres compromis architecturaux

| # | Tension | Décision | Prix accepté |
|---|---|---|---|
| C4 | Sécurité ⇄ UX/Performance | BCrypt lent (correct) + rate limiting | Légère friction au login |
| C5 | Verrou pessimiste ⇄ optimiste | Pessimiste (conflits fréquents en pic) | Latence sur points chauds |
| C6 | Hold gèle l'inventaire ⇄ disponibilité | TTL court (5 min) + worker auto | Disponibilité réduite 5 min |
| C7 | Exactitude rate limiting ⇄ simplicité | Local par nœud (approximatif) | Seuil global approximatif |
| C8 | Durabilité ⇄ performance d'écriture | Durabilité prioritaire | Latence écriture légèrement supérieure |

**Ce qu'on dit à l'oral :**  
> « On a aussi documenté les compromis secondaires. Le plus intéressant est C6 :
> quand un utilisateur fait une réservation, les chambres sont 'gelées' en HOLD
> pendant 5 minutes. C'est une tension entre bloquer l'inventaire et permettre
> à l'utilisateur de réfléchir avant de confirmer. On a choisi un TTL court
> et un worker qui libère automatiquement les holds expirés. »

---

## SLIDE 14 — Preuve de fonctionnement (démo / tests)

**Titre :** Preuve mesurée — les tests parlent

**Résultats des tests de concurrence :**

```
50 clients simultanés → 1 même slot
  ✅ 1 × 201 Created
  ✅ 49 × 409 Conflict
  ✅ 0 surréservation en base
```

**Tests automatisés (xUnit + PostgreSQL réel) :**
- `ConcurrencyTests` : 4 scénarios (race condition, expiration, double confirmation)
- `SmokeTests` : parcours nominal complet

**Scripts de recette :**
- `infra/concurrency-test.sh` — 6 requêtes parallèles sur le même slot
- `infra/fault-tolerance-test.sh` — panne d'un nœud en cours de route
- `infra/security-test.sh` — 401/403/429, injection SQL

**Ce qu'on dit à l'oral :**  
> « On ne fait pas que déclarer que le système est correct — on le prouve.
> Les tests de concurrence tournent sur une vraie base PostgreSQL, pas des mocks.
> 50 clients simultanés sur 1 chambre : exactement 1 succès, 49 conflits propres.
> On peut aussi montrer en direct : docker compose up, dotnet test, et les scripts de recette. »

---

## SLIDE 15 — Accessibilité et performance (livrables 2 et 3)

**Titre :** Accessibilité RGAA 4 et Performance — les deux autres livrables

**Performance (Livrable 2) :**
- Lecture (disponibilités) : p99 ≈ 26 ms (index dédié, lecture non bloquante)
- Écriture sous charge : tenue des 3 nœuds validée
- Goulot identifié : connexions PostgreSQL (pool Npgsql configuré)
- Protocole vers 500 utilisateurs : documenté

**Accessibilité RGAA 4 (Livrable 3) :**
- Audit outillé WAVE : 2 captures dans `livrables/img/`
- Front HTML sémantique : navigation clavier native
- Contraste vérifié (8,59:1 — WCAG AAA Pass), labels explicites, rôles ARIA
- **NC-2 (focus visible), NC-3 (aria-live messages), NC-5 (labels for/id) : corrigées dans le code**
- Non-conformités restantes : NC-1 (room-card non focusable clavier), NC-6 (erreurs non liées aux champs)

**Lien avec les contraintes du sujet :**
- Contrainte 3 : MFA + accessible au clavier → MFA côté API ✅, front navigable clavier ✅
- Contrainte 2 : 500 utilisateurs, < 2 s → mesuré et documenté ✅

**Ce qu'on dit à l'oral :**  
> « Les deux autres livrables sont le rapport de performance et le rapport d'accessibilité.
> Sur la performance, le chiffre clé est le p99 à 26 ms en lecture — 75× sous la cible de 2s.
> Sur l'accessibilité, on a fait un vrai audit WAVE et on a corrigé plusieurs non-conformités.
> Le choix d'un front HTML sémantique (ADR-006) facilite l'accessibilité par défaut —
> pas besoin de librairie, les contrôles natifs du navigateur font le travail. »

---

## SLIDE 16 — Les 5 contraintes du sujet : réponse architecturale

**Titre :** Les 5 contraintes du sujet — comment l'architecture y répond

| # | Contrainte | Réponse architecturale | Preuve |
|---|---|---|---|
| C1 | Déploiement mutualisé → cloud sans refonte | Docker + config 12-factor, aucune adhérence propriétaire, `docker-compose.aws.yml` fourni | `docker compose up` en une commande |
| C2 | 500 users simultanés, < 2 s | 3 nœuds stateless + Nginx round-robin, index dédiés | p99 ≈ 26 ms en lecture (Livrable 2) |
| C3 | MFA + interface accessible clavier | Endpoints TOTP `/api/auth/mfa/setup` + `/verify`, front HTML sémantique navigable clavier | Audit WAVE 0 erreur (Livrable 3) |
| C4 | Fonctionnel en faible bande passante | Service Worker : cache-first assets, network-first dispo avec timeout 5 s + fallback offline | `sw.js` — stratégies par type de requête |
| C5 | Localisation FR / EN / AR + RTL | `i18n.js` + fichiers JSON, `dir="rtl"` automatique sur `<html>` pour l'arabe | `frontend/locales/{fr,en,ar}.json` |
| C6 | Agrégats stats sans PII | Vues SQL `v_analytics_bookings_by_*` — GROUP BY uniquement, aucun user_id/email exposé | `AnalyticsRepository`, accès ADMIN seul |

**Ce qu'on dit à l'oral :**
> « Le sujet posait 6 contraintes transverses — voici comment l'architecture y répond
> concrètement, chacune avec une preuve.
>
> Sur le déploiement : tout est conteneurisé, aucune dépendance à un cloud spécifique.
> On a même un fichier docker-compose.aws.yml prêt.
>
> Sur la MFA : les endpoints sont implémentés côté API — setup TOTP, QR code compatible
> Google Authenticator, vérification TOTP RFC 6238. Dette documentée : le login ne force
> pas encore le code si MFA activée.
>
> Sur l'offline : le Service Worker applique une stratégie différente selon le type
> de requête. Les assets statiques sont en cache-first. Les disponibilités sont en
> network-first avec timeout 5 secondes puis fallback cache. Les mutations restent
> network-only — on ne cache jamais une écriture.
>
> Sur l'arabe : `dir="rtl"` sur `<html>`, le CSS s'adapte automatiquement.
>
> Sur les analytics : les vues SQL agrègent sans jamais joindre la table `users`. »

---

## SLIDE 17 — Risques, dette technique et trajectoire

**Titre :** Ce qu'on a assumé et la trajectoire

**Risques documentés (ADR + dossier §13) :**

| Risque | Mitigation prévue |
|---|---|
| BD = point de défaillance unique | Réplication + bascule en prod |
| Rate limiting approximatif (local) | Redis partagé (ADR-005) |
| Contention sur chambre très demandée | Partitionnement par établissement |
| MFA non enforced au login | Intégration dans `AuthService.LoginAsync` |

**Trajectoire (§14 du dossier) :**
- Court terme : correctifs accessibilité NC-1/NC-6, MFA complète
- Moyen terme : cache Redis, réplicas de lecture, CDN
- Long terme : partitionnement, Strangler Fig si microservices justifiés

**Ce qu'on dit à l'oral :**  
> « On est transparents sur ce qu'on n'a pas fini. Le rate limiting est approximatif,
> on le sait, on l'a documenté comme dette technique avec sa résolution dans l'ADR-005.
> La MFA côté API est implémentée mais pas encore enforced au login.
> Ces choix sont assumés et documentés — c'est ça l'honnêteté architecturale :
> ne pas prétendre que tout est parfait, mais documenter ce qui reste et pourquoi. »

---

## SLIDE 18 — Résumé : toutes les heuristiques et compromis

**Titre :** Récapitulatif — Heuristiques et compromis appliqués

| Heuristique / Méthode | Application dans le projet |
|---|---|
| **KISS** | Monolithe modulaire plutôt que microservices |
| **YAGNI** | Périmètre maîtrisé, évolutions documentées (pas préimplémentées) |
| **DRY** | Moteur de slots générique |
| **Couches + cohésion/couplage** | Architecture en 4 couches, DTO en frontière |
| **Repository Pattern** | Persistance abstraite et testable |
| **Défense en profondeur** | FOR UPDATE + index unique partiel |
| **Rendre les erreurs visibles** | Middleware d'erreurs, codes HTTP normalisés |
| **Build for today, design for change** | Trajectoire planifiée, architecture extensible |
| **Décisions difficiles = architecturales** | Stratégie de concurrence traitée en priorité (ADR-001) |
| **CAP → CP** | Cohérence forte, refus propre plutôt qu'incohérence |
| **ATAM / QOC** | Analyse des compromis C1-C8 documentée |
| **ADR** | 6 décisions tracées avec contexte, options, raisons, conséquences |

**Ce qu'on dit à l'oral :**  
> « Ce tableau résume tout. On n'a pas appliqué les heuristiques du cours de façon
> académique — on les a vraiment utilisées pour prendre des décisions concrètes.
> KISS nous a fait choisir le monolithe. YAGNI nous a empêchés de sur-ingénier.
> L'analyse CAP nous a positionnés CP. Les ADR tracent chaque décision difficile.
> C'est ça l'architecture : des choix assumés, documentés, et prouvés. »

---

## SLIDE 19 — Conclusion et démo live

**Titre :** Conclusion — un système correct, prouvé, documenté

**3 messages clés :**

1. **Le problème est distribué** — résolu là où c'est maîtrisé (PostgreSQL ACID)
2. **Les compromis sont documentés** — pas de magie, des choix assumés
3. **Les preuves sont exécutables** — `docker compose up && dotnet test`

**Démo live :**
```bash
docker compose up -d --build
# → http://localhost:8088/
dotnet test
# → 6/6 passés, dont tests de concurrence réels
```

**Ce qu'on dit à l'oral :**  
> « Pour conclure : notre plateforme garantit zéro surréservation dans un contexte
> distribué, grâce à deux barrières indépendantes et une architecture CP assumée.
> Tous les compromis sont documentés dans les ADR et le dossier d'architecture.
> Et tout est vérifiable : une commande lance la stack, une autre lance les tests.
> On peut faire une démo en direct si vous le souhaitez. Merci. »

---

## Conseils de préparation

**Timing suggéré (25-30 min) :**
- Slides 1-2 : 2 min (contexte, accroche)
- Slides 3-5 : 4 min (architecture, KISS, YAGNI)
- Slides 6-9 : 6 min (cœur du sujet : anti-surréservation, compromis CAP/C1/C3)
- Slides 10-13 : 4 min (heuristiques code, modèle, autres compromis)
- Slides 14-15 : 3 min (preuves, accessibilité/performance)
- **Slide 16 : 3 min (5 contraintes du sujet)**
- Slides 17-19 : 4 min (risques, synthèse, conclusion)

**Questions probables du jury et éléments de réponse :**

| Question | Réponse clé |
|---|---|
| Pourquoi pas Redis pour le verrouillage ? | PostgreSQL déjà présent, ACID natif, pas de dépendance supplémentaire (ADR-001) |
| Que se passe-t-il si la base tombe ? | Écriture impossible (CP assumé) ; réplication + bascule en prod (risque documenté) |
| Comment tu prouves les 500 users simultanés ? | Protocole dans Livrable 2 ; tests de concurrence sur base réelle |
| Pourquoi un index WHERE active = TRUE ? | Pour permettre plusieurs lignes inactives (annulées/expirées) sur le même slot, mais une seule active |
| La MFA est vraiment implémentée ? | Oui côté API — TOTP RFC 6238, endpoints setup/verify, compatible Google Authenticator. Pas encore enforced au login — dette documentée |
| Comment gérez-vous le déploiement cloud ? | Docker + config 12-factor, aucune adhérence propriétaire. `docker-compose.aws.yml` fourni, migration vers ECS/Kubernetes sans refonte |
| Comment gérez-vous l'offline ? | Service Worker : cache-first assets (page disponible sans réseau), network-first avec timeout 5s pour les disponibilités. Mutations = network-only |
| Et l'arabe (RTL) ? | `i18n.js` + fichiers JSON par langue. Pour l'arabe : `dir="rtl"` sur `<html>`, le CSS s'adapte automatiquement |
| Comment anonymisez-vous les stats ? | Vues SQL `v_analytics_bookings_by_room/week` — GROUP BY uniquement, aucun `user_id`/email dans les résultats. Endpoint réservé ADMIN |
| Et si un attaquant fait 100 req/s ? | Rate limiting local → seuil approximatif derrière round-robin. Assumé, documenté ADR-005. Résolution : Redis partagé en backlog |
| Comment scaler l'écriture à 10k req/s ? | Partitionnement par établissement/région, réplicas de lecture. Trajectoire documentée §14 |
| Qu'est-ce que l'ATAM ? | Méthode d'analyse des attributs de qualité et de leurs tensions ; appliquée aux compromis C1-C8 |
