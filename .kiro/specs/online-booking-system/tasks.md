# Implementation Plan: Système de réservation en ligne distribué

## Overview

Plan d'implémentation ordonné. Chaque tâche est incrémentale, vérifiable, et tracée vers les exigences. Ordre logique : fondations → cœur métier → concurrence → distribution → sécurité → déploiement → validation.

## Tasks

### 1. Fondations du projet et du stockage

- [x] 1.1 Initialiser la structure du projet et l'outillage
  - Créer l'arborescence (service API, migrations, infra, tests, docs)
  - Configurer le gestionnaire de dépendances et le linter
  - _Requirements: 10.4_

- [x] 1.2 Définir le schéma de base de données et les migrations
  - Créer les tables `resource_slots`, `bookings`, `booking_slots`
  - Poser les contraintes d'unicité (`UNIQUE(slot_id)`, `UNIQUE(resource_id, period)`)
  - Script de migration exécutable et versionné
  - _Requirements: 2.3, 3.3, 7.3_

- [x] 1.3 Implémenter la couche d'accès aux données (repository) avec requêtes paramétrées
  - Connexion à PostgreSQL avec pool
  - Toutes les requêtes paramétrées (anti-injection)
  - _Requirements: 9.2_

### 2. Cœur métier — consultation et réservation

- [x] 2.1 Implémenter la consultation de disponibilité
  - `findAvailable(type, period)` excluant les slots HELD/BOOKED
  - Endpoint `GET /api/availability`
  - _Requirements: 1.1, 1.2, 1.3_

- [x] 2.2 Implémenter la création de réservation atomique (hold)
  - Transaction `BEGIN ... FOR UPDATE ... COMMIT`
  - Rejet total si une ressource indisponible (tout ou rien)
  - Génération d'un identifiant de réservation
  - Endpoint `POST /api/bookings`
  - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5_

- [x] 2.3 Implémenter la confirmation de réservation
  - Transition HOLD → CONFIRMED avant expiration
  - Endpoint `POST /api/bookings/{id}/confirm`
  - _Requirements: 4.4_

- [x] 2.4 Implémenter l'annulation de réservation
  - Libération atomique des slots, transition → CANCELLED
  - Erreur explicite si inexistante/déjà annulée
  - Endpoint `DELETE /api/bookings/{id}`
  - _Requirements: 5.1, 5.2, 5.3_

### 3. Concurrence et expiration

- [x] 3.1 Implémenter le worker d'expiration des holds
  - Tâche périodique `releaseExpiredHolds()`
  - Hold TTL configurable (défaut 5 min)
  - _Requirements: 4.1, 4.2, 4.3_

- [x] 3.2 ✅ CHECKPOINT — Test de concurrence anti-surréservation
  - Lancer K requêtes parallèles sur le même slot, asserter qu'exactement 1 réussit
  - Vérifier l'invariant Property 2 (au plus 1 réservation active par slot)
  - _Requirements: 3.1, 3.2, 3.3, 3.4_

### 4. Architecture distribuée

- [x] 4.1 Rendre le service applicatif stateless et réplicable
  - Aucun état en mémoire propre à un nœud ; tout état partagé en base
  - Endpoint `GET /health`
  - _Requirements: 6.1, 6.3, 11.1_

- [x] 4.2 Configurer le répartiteur de charge Nginx
  - Round-robin sur N nœuds API + health checks
  - _Requirements: 6.2_

- [x] 4.3 ✅ CHECKPOINT — Test multi-nœuds et tolérance aux pannes
  - Vérifier le routage sur plusieurs nœuds
  - Tuer un nœud sous charge : continuité de service + aucune réservation partielle
  - _Requirements: 6.4, 7.1, 7.2, 7.4_

### 5. Sécurité (critères 1.2 et 1.3)

- [x] 5.1 Implémenter l'authentification JWT
  - Middleware exigeant un jeton valide sur les endpoints de réservation
  - 401 si absent/invalide/expiré
  - Stockage chiffré des secrets
  - _Requirements: 8.1, 8.2, 8.4_

- [x] 5.2 Implémenter le contrôle d'accès (ownership)
  - 403 si modification/annulation d'une réservation d'autrui
  - _Requirements: 8.3_

- [x] 5.3 Implémenter la validation d'entrée et le rate limiting
  - 400 sur entrée malformée ; limitation par client (429)
  - Journalisation des anomalies de sécurité
  - _Requirements: 9.1, 9.3, 9.4_

- [x] 5.4 Produire les tests de sécurité et documenter les anomalies
  - Cas : injection SQL, jeton manquant/expiré, accès non autorisé, dépassement de débit
  - Rapport des anomalies identifiées + préconisation d'outils de protection
  - _Requirements: 9.2, 9.4_ (critères grille 1.2, 1.3)

### 6. Observabilité

- [x] 6.1 Implémenter la journalisation des événements de réservation
  - Log horodaté : création, confirmation, annulation, expiration, conflit
  - _Requirements: 11.2, 11.3_

### 7. Automatisation du déploiement (critère 1.1)

- [x] 7.1 Écrire les Dockerfiles et le docker-compose
  - Services : nginx, api (scalable), postgres, redis, job de migration
  - _Requirements: 10.2_

- [x] 7.2 ✅ CHECKPOINT — Déploiement en une commande sur machine propre
  - `docker compose up` provisionne et démarre tout sans intervention manuelle
  - Vérifier via un smoke test end-to-end
  - _Requirements: 10.1, 10.3_

### 10. Interface web (front)

- [x] 10.1 Créer le front statique (HTML/CSS/JS) auth + réservation
  - Écrans : connexion/inscription, recherche de disponibilité, réservation, mes réservations
  - Conservation du JWT côté client, appels vers `/api/*`
  - _Requirements: 14.1, 14.2, 14.4, 14.5_

- [x] 10.2 Servir le front via Nginx sur la même origine que l'API
  - Nginx sert les fichiers statiques et proxifie `/api/` et `/health`
  - _Requirements: 14.3_

### 8. Documentation (critère 1.4)

- [x] 8.1 Rédiger la documentation technique
  - Architecture, composants, flux, choix atomicité/cohérence
  - _Requirements: 12.1, 12.3_

- [x] 8.2 Rédiger la documentation d'usage et l'API
  - Installation, démarrage, endpoints, paramètres, codes de réponse
  - _Requirements: 12.2, 12.4_

### 9. Validation finale (critère 1.5)

- [x] 9.1 Rédiger le cahier de recette
  - Cas de test rattachés aux exigences, format attendu vs obtenu
  - Inclure scénario de concurrence (9.2) et de tolérance aux pannes
  - _Requirements: 13.1, 13.2, 13.3_

- [x] 9.2 ✅ CHECKPOINT FINAL — Exécuter le cahier de recette
  - Rejouer tous les cas, consigner les résultats reproductibles
  - Vérifier la couverture de tous les requirements et des Correctness Properties
  - _Requirements: 13.4_

## Task Dependency Graph

```
1.1 ─► 1.2 ─► 1.3 ─┬─► 2.1
                   ├─► 2.2 ─► 2.3
                   │         └─► 2.4
                   └─► 2.2 ─► 3.1 ─► 3.2 (checkpoint concurrence)
2.x ─► 4.1 ─► 4.2 ─► 4.3 (checkpoint multi-nœuds / pannes)
2.x ─► 5.1 ─► 5.2
       5.1 ─► 5.3 ─► 5.4
2.x ─► 6.1
4.x + 5.x ─► 7.1 ─► 7.2 (checkpoint déploiement)
tout ─► 8.1, 8.2 (documentation)
tout ─► 9.1 ─► 9.2 (checkpoint final / recette)
```

Vagues d'exécution parallélisables :

```json
{
  "waves": [
    { "wave": 1, "tasks": ["1.1", "1.2", "1.3"], "parallel": false, "description": "Fondations projet et stockage (séquentiel)" },
    { "wave": 2, "tasks": ["2.1", "2.2"], "parallel": true, "description": "Consultation et création de réservation" },
    { "wave": 3, "tasks": ["2.3", "2.4", "3.1"], "parallel": true, "description": "Confirmation, annulation, expiration" },
    { "wave": 4, "tasks": ["3.2"], "parallel": false, "description": "Checkpoint concurrence anti-surréservation" },
    { "wave": 5, "tasks": ["4.1", "5.1", "5.3", "6.1"], "parallel": true, "description": "Distribution, sécurité, observabilité (indépendants)" },
    { "wave": 6, "tasks": ["4.2", "5.2", "5.4"], "parallel": true, "description": "Load balancer, ownership, tests de sécurité" },
    { "wave": 7, "tasks": ["4.3"], "parallel": false, "description": "Checkpoint multi-nœuds et tolérance aux pannes" },
    { "wave": 8, "tasks": ["7.1"], "parallel": false, "description": "Dockerfiles et compose" },
    { "wave": 9, "tasks": ["7.2"], "parallel": false, "description": "Checkpoint déploiement une commande" },
    { "wave": 10, "tasks": ["8.1", "8.2"], "parallel": true, "description": "Documentation technique et d'usage" },
    { "wave": 11, "tasks": ["9.1"], "parallel": false, "description": "Rédaction du cahier de recette" },
    { "wave": 12, "tasks": ["9.2"], "parallel": false, "description": "Checkpoint final - exécution de la recette" }
  ]
}
```

## Notes

- Les tâches marquées ✅ CHECKPOINT sont des points de validation obligatoires : ne pas avancer tant que le test associé n'est pas vert.
- **Leçon de méthodologie** : une spec complète garantit qu'on construit la bonne chose, pas que le code compile ou tourne. Après chaque tâche, exécuter réellement build + tests avant de la cocher. Ne jamais cocher une tâche sur la seule base de la couverture des requirements.
- Le test de concurrence (3.2) et le test de pannes (4.3) sont les preuves centrales du sujet (atomicité + cohérence distribuée) et de la grille — à ne pas négliger.
- La stack (langage applicatif) reste un choix ouvert ; l'invariant anti-surréservation tient tant que la coordination passe par un point transactionnel unique (PostgreSQL).
