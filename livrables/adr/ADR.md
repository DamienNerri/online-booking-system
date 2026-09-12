# Architecture Decision Records (ADR)

Journal des décisions d'architecture, au format simplifié recommandé par le cours
(Contexte / Options / Décision / Raisons / Conséquences). Les ADR rendent
explicite le raisonnement et les compromis (méthode QOC : Questions, Options,
Criteria).

---

## ADR-001 — PostgreSQL + `FOR UPDATE` comme coordinateur de concurrence

**Statut :** accepté

**Contexte.** Le système doit garantir l'absence de surréservation malgré des
accès concurrents et plusieurs nœuds applicatifs. Il faut un point de
sérialisation fiable des opérations conflictuelles.

**Options (QOC).**
1. Protocole de consensus applicatif maison (verrous en mémoire coordonnés).
2. Verrou distribué externe (Redis Redlock, etcd, Zookeeper).
3. Transactions ACID de PostgreSQL avec `SELECT … FOR UPDATE`, doublées d'un
   index unique partiel.

**Critères.** Correction, simplicité opérationnelle, nombre de dépendances,
adéquation à une équipe de 3, performance.

**Décision.** Option 3.

**Raisons.**
- Correction éprouvée (ACID, verrouillage au niveau ligne).
- Aucun composant d'infrastructure supplémentaire à exploiter.
- Un seul point de vérité, facile à raisonner.

**Conséquences.**
- La base devient le point de contention en écriture (compromis assumé, cf.
  tension C3). À grande échelle : partitionnement par établissement/région,
  réplicas de lecture pour la disponibilité.
- Filet de sécurité : index unique partiel `uq_active_slot`.

---

## ADR-002 — Monolithe modulaire distribué plutôt que microservices

**Statut :** accepté

**Contexte.** Équipe de 3 personnes, domaine métier simple et stable, besoin de
scalabilité modérée, expertise limitée en systèmes distribués.

**Décision.** Monolithe en couches (Repository / Service / Endpoints), déployé en
plusieurs réplicas **stateless** derrière Nginx.

**Raisons.** Application directe du guide de sélection du cours (équipe < 10,
domaine simple, performance/latence critique, ACID natif). Les microservices
imposeraient des transactions distribuées (Saga) pour l'anti-surréservation —
complexité injustifiée.

**Conséquences.** Simplicité de développement/déploiement et bonnes performances
(pas de latence réseau interne). Extraction de services différée ; si besoin,
appliquer un pattern Strangler Fig et « Branch by Abstraction ».

---

## ADR-003 — Nœuds applicatifs stateless + authentification JWT

**Statut :** accepté

**Contexte.** Scalabilité horizontale et tolérance aux pannes attendues (BNF2).

**Décision.** Aucun état de session côté nœud ; authentification par jeton JWT
signé (HMAC). Mots de passe hachés avec BCrypt.

**Conséquences.** Nœuds interchangeables (un nœud peut tomber sans perte d'état ;
Nginx route vers les nœuds vivants). Le secret JWT devient sensible : géré **hors
du code** (user-secrets en développement, variable d'environnement `Jwt__Secret`
en production), avec validation obligatoire au démarrage.

---

## ADR-004 — Verrou pessimiste plutôt qu'optimiste

**Statut :** accepté

**Contexte.** En pic de demande, les conflits sur une même chambre/date sont
fréquents.

**Décision.** Verrouillage **pessimiste** (`SELECT … FOR UPDATE`) dès la lecture
des slots visés.

**Raisons.** Quand les conflits sont fréquents, l'échec immédiat et clair (409)
est préférable à une logique de réessai (optimiste), plus coûteuse et complexe
côté client.

**Conséquences.** Verrous détenus le temps de la transaction → transactions
maintenues **minimales**. L'index unique partiel reste le filet ultime.

---

## ADR-005 — Rate limiting local par nœud (Redis en cible)

**Statut :** accepté (temporaire)

**Contexte.** Protéger l'API contre les abus (BNF3) dans une architecture à
plusieurs nœuds.

**Décision.** Comptage **local par nœud** dans cette version.

**Raisons.** Simplicité, aucune dépendance supplémentaire.

**Conséquences.** Seuil global **approximatif** derrière un répartiteur
round-robin (jusqu'à N×seuil). Évolution identifiée : compteur partagé (Redis)
pour un plafonnement exact.

---

## ADR-006 — Front statique léger (pas de framework SPA lourd)

**Statut :** accepté

**Contexte.** Contraintes d'accessibilité (BNF3) et de qualité dégradée en
connectivité faible (BNF4).

**Décision.** Front en HTML sémantique + CSS + JavaScript vanilla, servi sur la
même origine que l'API (via Nginx).

**Raisons.** Robustesse (peu de dépendances), accessibilité facilitée (HTML
natif, navigation clavier), poids réseau minimal, pas de problème de CORS.

**Conséquences.** Moins d'outillage « moderne » (pas de composants riches
préfabriqués). Rendu XSS-safe assuré manuellement (`textContent`, pas
d'`innerHTML` sur données serveur). Piste offline : Service Worker + cache.
