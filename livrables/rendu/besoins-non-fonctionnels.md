# Besoins non fonctionnels

Système de réservation en ligne distribué — Bloc 5 (Traitements distribués).
Ce document décrit **comment** le système doit se comporter : qualités attendues,
contraintes et exigences transverses.

## BNF1 — Cohérence et intégrité des données
- Le système doit garantir l'**atomicité** des réservations : une opération est
  soit entièrement appliquée, soit annulée (aucun état partiel).
- Le système doit garantir l'**absence de surréservation** : au plus une
  réservation active par unité de ressource et par période, **même sous accès
  concurrents**.
- L'état des ressources doit rester **cohérent** entre tous les nœuds.
- *Mise en œuvre :* transactions ACID PostgreSQL, `SELECT … FOR UPDATE`, index unique partiel.

## BNF2 — Concurrence
- Le système doit traiter correctement des **demandes simultanées** sur la même
  ressource, en sérialisant les accès conflictuels.
- Les demandes perdantes doivent être **refusées proprement** (message d'indisponibilité), sans blocage ni erreur technique.
- *Preuve :* 30 requêtes parallèles → 1 succès, 29 conflits (`infra/concurrency-test.ps1`).

## BNF3 — Disponibilité et tolérance aux pannes
- Le service doit rester **disponible** malgré la défaillance d'un nœud applicatif.
- Aucune réservation ne doit être perdue ni laissée partielle en cas de panne d'un nœud.
- Un nœud redémarré doit pouvoir **rejoindre le cluster**.
- *Mise en œuvre :* nœuds **stateless**, répartiteur Nginx routant vers les nœuds vivants.
- *Preuve :* `infra/fault-tolerance-test.ps1`.

## BNF4 — Scalabilité (montée en charge)
- L'architecture doit permettre d'**ajouter des nœuds applicatifs** pour absorber
  davantage de trafic (mise à l'échelle horizontale).
- Les nœuds doivent être **interchangeables** (sans état local).
- *Mise en œuvre :* `deploy.replicas` dans docker-compose, répartition round-robin.

## BNF5 — Performance
- Une recherche de disponibilité doit répondre **rapidement** (objectif < 500 ms
  pour un inventaire de l'ordre de 10 000 ressources).
- *Mise en œuvre :* index de recherche (`resource_type`, `status`, période), pool de connexions.

## BNF6 — Sécurité
- **Authentification** obligatoire (JWT) pour toute opération de réservation.
- **Autorisation** : un utilisateur n'agit que sur ses propres réservations (403 sinon).
- **Mots de passe** stockés hachés (BCrypt), jamais en clair.
- **Anti-injection SQL** : toutes les requêtes sont paramétrées.
- **Limitation de débit** (rate limiting) par client pour freiner les abus (429).
- **Journalisation** des anomalies de sécurité.
- *Preuve :* `infra/security-test.ps1`.
- *Limites/perspectives prod :* TLS au répartiteur, secrets dans un coffre, rate limiting partagé (Redis).

## BNF7 — Durabilité
- Les réservations confirmées doivent **survivre au redémarrage** d'un nœud ou de la base.
- *Mise en œuvre :* persistance PostgreSQL sur volume Docker.

## BNF8 — Observabilité
- Chaque nœud expose un **point de santé** (`/health`).
- Les événements de réservation (création, confirmation, annulation, expiration, conflit) sont **journalisés** avec horodatage.

## BNF9 — Déploiement et exploitabilité
- L'ensemble de l'architecture doit se déployer en **une seule commande**, sans étape manuelle.
- Les migrations de base et les données initiales s'appliquent **automatiquement** au démarrage.
- La configuration d'infrastructure est **versionnée** (Infrastructure as Code).
- *Mise en œuvre :* `docker compose up -d --build`.

## BNF10 — Maintenabilité et documentation
- Le code est organisé en couches (endpoints, services, repositories, middleware).
- Le projet fournit une **documentation technique** et une **documentation d'usage**.
- La démarche est traçable : exigences (EARS) → design → tâches → cahier de recette.

## BNF11 — Portabilité
- Le système doit fonctionner sur toute machine disposant de **Docker**, sans
  installation d'outils supplémentaires (le SDK .NET n'est pas requis : build en conteneur).

## Traçabilité vers les exigences

| Besoin | Requirements |
|---|---|
| BNF1 Cohérence | 2, 3, 6.3 |
| BNF2 Concurrence | 3 |
| BNF3 Disponibilité/pannes | 6, 7 |
| BNF4 Scalabilité | 6.1 |
| BNF5 Performance | 1.4 |
| BNF6 Sécurité | 8, 9 |
| BNF7 Durabilité | 7.3 |
| BNF8 Observabilité | 11 |
| BNF9 Déploiement | 10 |
| BNF10 Maintenabilité/doc | 12, 13 |
| BNF11 Portabilité | 10 |
