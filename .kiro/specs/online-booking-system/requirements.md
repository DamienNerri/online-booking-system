# Requirements Document

## Introduction

Ce document formalise les exigences d'un **système de réservation en ligne distribué** (hôtels / vols), conformément au sujet n°9 du projet EIDAL Bloc 5 « Introduction aux traitements distribués ».

Le cœur du problème est la **gestion de ressources limitées** (chambres, sièges) accessibles simultanément par de nombreux clients, répartie sur **plusieurs nœuds**. Le système doit garantir :
- l'**atomicité** des transactions de réservation,
- la **cohérence** de l'état des ressources malgré les mises à jour concurrentes,
- l'**absence de surréservation** (double booking) d'une même unité de ressource,
- la **tolérance aux pannes** d'un nœud.

Les exigences couvrent aussi les critères de la grille d'évaluation : automatisation du déploiement (1.1), tests de sécurité (1.2), outils de protection (1.3), documentation (1.4), protocole de validation et cahier de recette (1.5).

## Glossary
- **Ressource** : unité réservable atomique (une chambre pour une nuit donnée, un siège sur un vol donné).
- **Inventaire** : ensemble des ressources disponibles et de leur état.
- **Réservation** : engagement d'un client sur une ou plusieurs ressources pour une période donnée.
- **Nœud** : instance de service participant au système distribué.
- **Coordinateur** : composant responsable de l'atomicité d'une transaction distribuée.

## Requirements

### Requirement 1: Consultation de l'inventaire

**User Story:** En tant que client, je veux consulter les ressources disponibles pour une période, afin de choisir ce que je réserve.

#### Acceptance Criteria
1. WHEN un client soumet une requête de recherche avec une période et un type de ressource, THE Système SHALL retourner la liste des ressources disponibles sur cette période.
2. WHEN une ressource est déjà réservée (confirmée) pour la période demandée, THE Système SHALL l'exclure des résultats de disponibilité.
3. WHEN aucune ressource n'est disponible, THE Système SHALL retourner une liste vide avec un code de succès (et non une erreur).
4. THE Système SHALL retourner les résultats de recherche en moins de 500 ms pour un inventaire de 10 000 ressources.

---

### Requirement 2: Création d'une réservation atomique

**User Story:** En tant que client, je veux réserver une ou plusieurs ressources en une seule opération, afin que ma réservation soit soit complètement acceptée, soit complètement refusée.

#### Acceptance Criteria
1. WHEN un client soumet une demande de réservation portant sur une ou plusieurs ressources, THE Système SHALL traiter l'opération de manière atomique (tout ou rien).
2. IF au moins une des ressources demandées n'est plus disponible, THEN THE Système SHALL rejeter l'intégralité de la réservation et ne réserver aucune ressource.
3. WHEN une réservation est acceptée, THE Système SHALL passer l'état de chaque ressource concernée à « réservée » et persister la réservation.
4. WHEN une réservation est acceptée, THE Système SHALL retourner un identifiant de réservation unique.
5. IF la transaction échoue à n'importe quelle étape, THEN THE Système SHALL restaurer l'état antérieur de toutes les ressources concernées.

---

### Requirement 3: Prévention de la surréservation sous concurrence

**User Story:** En tant qu'exploitant, je veux qu'une même unité de ressource ne puisse jamais être réservée deux fois, afin d'éviter les conflits clients.

#### Acceptance Criteria
1. WHEN deux clients ou plus demandent simultanément la même ressource pour une période chevauchante, THE Système SHALL accorder la ressource à au plus un seul client.
2. WHEN un conflit de concurrence est détecté sur une ressource, THE Système SHALL rejeter les demandes perdantes avec une erreur « ressource indisponible ».
3. THE Système SHALL garantir qu'aucune séquence d'opérations concurrentes ne produit d'état de surréservation (invariant : au plus 1 réservation active par unité de ressource et par période).
4. WHEN un client détient une réservation temporaire (hold) non confirmée, THE Système SHALL empêcher tout autre client de réserver la même ressource pendant la durée du hold.

---

### Requirement 4: Expiration des réservations temporaires (hold)

**User Story:** En tant qu'exploitant, je veux que les réservations non confirmées expirent, afin de libérer les ressources bloquées.

#### Acceptance Criteria
1. WHEN un client initie une réservation, THE Système SHALL créer un hold d'une durée configurable (par défaut 5 minutes).
2. WHILE un hold est actif, THE Système SHALL considérer la ressource comme indisponible pour les autres clients.
3. WHEN le délai du hold expire sans confirmation, THE Système SHALL libérer automatiquement la ressource et la rendre à nouveau disponible.
4. WHEN un client confirme sa réservation avant expiration, THE Système SHALL convertir le hold en réservation confirmée.

---

### Requirement 5: Annulation d'une réservation

**User Story:** En tant que client, je veux annuler une réservation confirmée, afin de libérer les ressources.

#### Acceptance Criteria
1. WHEN un client demande l'annulation d'une réservation existante, THE Système SHALL libérer les ressources associées de manière atomique.
2. IF la réservation n'existe pas ou est déjà annulée, THEN THE Système SHALL retourner une erreur explicite sans modifier d'état.
3. WHEN une annulation réussit, THE Système SHALL passer l'état de la réservation à « annulée » et rendre les ressources disponibles.

---

### Requirement 6: Architecture distribuée multi-nœuds

**User Story:** En tant qu'architecte, je veux répartir le système sur plusieurs nœuds, afin de démontrer le traitement distribué et de supporter la montée en charge.

#### Acceptance Criteria
1. THE Système SHALL être composé d'au moins deux instances de service applicatif pouvant traiter des requêtes en parallèle.
2. WHEN une requête arrive, THE répartiteur de charge SHALL la router vers un nœud disponible.
3. THE Système SHALL maintenir un état de réservation cohérent et partagé entre tous les nœuds.
4. WHEN plusieurs nœuds accèdent à la même ressource, THE Système SHALL sérialiser les accès concurrents via un mécanisme de coordination (verrou distribué ou transaction sur stockage partagé).

---

### Requirement 7: Tolérance aux pannes

**User Story:** En tant qu'exploitant, je veux que le système survive à la panne d'un nœud, afin d'assurer la disponibilité du service.

#### Acceptance Criteria
1. IF un nœud applicatif tombe en panne, THEN THE Système SHALL continuer à traiter les requêtes via les nœuds restants.
2. IF un nœud tombe en panne pendant une transaction non validée, THEN THE Système SHALL garantir qu'aucune réservation partielle n'est persistée.
3. THE Système SHALL persister l'état des réservations de manière durable (survivant à un redémarrage).
4. WHEN un nœud défaillant redémarre, THE Système SHALL lui permettre de rejoindre le cluster et de resynchroniser son état.

---

### Requirement 8: Authentification et contrôle d'accès

**User Story:** En tant qu'exploitant, je veux que seuls les utilisateurs authentifiés puissent réserver, afin de sécuriser le système. *(Couvre le critère de sécurité 1.3.)*

#### Acceptance Criteria
1. WHEN un utilisateur accède à un endpoint de réservation, THE Système SHALL exiger un jeton d'authentification valide.
2. IF le jeton est absent, invalide ou expiré, THEN THE Système SHALL rejeter la requête avec un code 401.
3. WHEN un utilisateur tente d'annuler une réservation qui ne lui appartient pas, THE Système SHALL rejeter la requête avec un code 403.
4. THE Système SHALL stocker les secrets (mots de passe, clés) de manière chiffrée et jamais en clair.

---

### Requirement 9: Robustesse des entrées et sécurité applicative

**User Story:** En tant qu'exploitant, je veux que le système résiste aux entrées malveillantes, afin de protéger les données. *(Couvre les critères 1.2 et 1.3.)*

#### Acceptance Criteria
1. WHEN une requête contient des paramètres invalides ou malformés, THE Système SHALL la rejeter avec un code 400 et un message explicite.
2. THE Système SHALL utiliser des requêtes paramétrées pour tout accès à la base de données (protection contre l'injection SQL).
3. THE Système SHALL limiter le nombre de requêtes par client sur une fenêtre de temps (rate limiting).
4. WHEN une anomalie de sécurité est détectée, THE Système SHALL la journaliser avec horodatage et contexte.

---

### Requirement 10: Automatisation du déploiement

**User Story:** En tant qu'architecte, je veux automatiser la mise en place de l'architecture, afin de la déployer de façon reproductible. *(Couvre le critère 1.1.)*

#### Acceptance Criteria
1. THE Système SHALL être déployable via une commande unique (ex. `docker compose up` ou script d'orchestration).
2. THE déploiement SHALL provisionner tous les composants : nœuds applicatifs, stockage partagé, répartiteur de charge, coordinateur.
3. WHEN le déploiement est lancé sur une machine propre, THE Système SHALL démarrer sans intervention manuelle supplémentaire.
4. THE Système SHALL versionner sa configuration d'infrastructure dans le dépôt (Infrastructure as Code).

---

### Requirement 11: Observabilité

**User Story:** En tant qu'exploitant, je veux suivre l'état du système, afin de détecter les incidents.

#### Acceptance Criteria
1. THE Système SHALL exposer un endpoint de santé (health check) pour chaque nœud.
2. THE Système SHALL journaliser les événements de réservation (création, confirmation, annulation, expiration, conflit).
3. WHEN un conflit de concurrence ou une erreur survient, THE Système SHALL en conserver une trace horodatée exploitable.

---

### Requirement 12: Documentation technique et d'usage

**User Story:** En tant qu'évaluateur, je veux une documentation complète, afin de comprendre, utiliser et faire évoluer la solution. *(Couvre le critère 1.4.)*

#### Acceptance Criteria
1. THE projet SHALL fournir une documentation technique décrivant l'architecture, les composants et les flux.
2. THE projet SHALL fournir une documentation d'usage (installation, démarrage, appels API).
3. THE projet SHALL documenter les choix de conception liés à l'atomicité et à la cohérence.
4. THE API SHALL être documentée (endpoints, paramètres, codes de réponse).

---

### Requirement 13: Protocole de validation et cahier de recette

**User Story:** En tant qu'évaluateur, je veux un protocole de validation, afin de vérifier objectivement que le système répond aux exigences. *(Couvre le critère 1.5.)*

#### Acceptance Criteria
1. THE projet SHALL fournir un cahier de recette listant les cas de test rattachés aux exigences.
2. THE cahier de recette SHALL inclure un scénario de test de concurrence prouvant l'absence de surréservation.
3. THE cahier de recette SHALL inclure un scénario de tolérance aux pannes (arrêt d'un nœud).
4. WHEN les tests de recette sont exécutés, THE résultat SHALL être reproductible et documenté (attendu vs obtenu).


### Requirement 14: Interface web utilisateur

**User Story:** En tant que client, je veux une interface web, afin d'utiliser le système sans outil technique.

#### Acceptance Criteria
1. THE Système SHALL servir une interface web permettant l'inscription et la connexion.
2. WHEN un utilisateur est connecté, THE interface SHALL permettre de rechercher des disponibilités, réserver, confirmer et annuler.
3. THE interface SHALL être servie par le répartiteur Nginx, sur la même origine que l'API (pas de CORS).
4. WHEN une opération échoue, THE interface SHALL afficher un message d'erreur explicite issu de l'API.
5. THE interface SHALL conserver le jeton d'authentification côté client et l'envoyer sur les appels protégés.
