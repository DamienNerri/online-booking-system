# Parcours utilisateurs détaillés

Système de réservation en ligne distribué — Bloc 5 (Traitements distribués).
Chaque parcours décrit : l'acteur, les préconditions, le déroulé nominal, les
variantes/erreurs, et les **contraintes** applicables (règles de gestion, délais,
sécurité, codes de réponse).

## Légende des contraintes
- 🔒 **Sécurité / autorisation**
- ⏱️ **Délai / temporel**
- ⚖️ **Règle de gestion**
- 🔁 **Concurrence**
- ↩️ **Code de réponse / comportement attendu**

---

## P1 — Créer un compte

- **Acteur :** Visiteur
- **Préconditions :** aucune ; l'utilisateur n'est pas connecté.
- **Déclencheur :** clic sur « Créer un compte ».

**Déroulé nominal**
1. L'utilisateur saisit un email et un mot de passe.
2. Le système valide le format et crée le compte (rôle *CLIENT*).
3. Le système renvoie un **jeton d'authentification (JWT)** ; l'utilisateur est connecté.

**Variantes / erreurs**
- Email déjà utilisé → ↩️ **409** « Un compte existe déjà pour cet email. »
- Email invalide ou mot de passe trop court → ↩️ **400** message explicite.

**Contraintes**
- ⚖️ Mot de passe **≥ 8 caractères**.
- 🔒 Mot de passe **haché** (BCrypt), jamais stocké en clair.
- ⚖️ Email **unique**, normalisé en minuscules.

---

## P2 — Se connecter

- **Acteur :** Client possédant un compte.
- **Préconditions :** compte existant.
- **Déclencheur :** clic sur « Se connecter ».

**Déroulé nominal**
1. Saisie email + mot de passe.
2. Le système vérifie les identifiants et renvoie un **JWT**.
3. Le jeton est conservé côté client et envoyé sur les appels protégés.

**Variantes / erreurs**
- Identifiants incorrects → ↩️ **401** « Email ou mot de passe invalide. »
  (message neutre : ne révèle pas si l'email existe).

**Contraintes**
- 🔒 Jeton valable **60 minutes** ; au-delà, reconnexion nécessaire.
- 🔒 Aucune information sensible dans les messages d'erreur.

---

## P3 — Rechercher des disponibilités

- **Acteur :** Client (connecté ou non — la recherche est ouverte).
- **Préconditions :** aucune.
- **Déclencheur :** clic sur « Rechercher » (type + période).

**Déroulé nominal**
1. Le client choisit un **type** (chambre / siège) et une **période** (du / au).
2. Le système renvoie la liste des **ressources libres** sur la période.
3. Le client visualise les résultats et sélectionne une ou plusieurs ressources.

**Variantes / erreurs**
- Aucune disponibilité → ↩️ **200** avec **liste vide** (pas une erreur).
- Dates au mauvais format ou fin ≤ début → ↩️ **400** message explicite.

**Contraintes**
- ⚖️ Seules les ressources **AVAILABLE** sont retournées (les *tenues* et *réservées* sont exclues).
- ⏱️ Format de date **`yyyy-MM-dd`** ; date de fin **strictement postérieure** au début.
- 📈 Objectif de réponse **< 500 ms**.

---

## P4 — Réserver (créer un hold)

- **Acteur :** Client **connecté**.
- **Préconditions :** au moins une ressource disponible sélectionnée.
- **Déclencheur :** clic sur « Réserver la sélection ».

**Déroulé nominal**
1. Le client envoie la liste des ressources à réserver.
2. Le système **verrouille** les ressources visées et vérifie leur disponibilité.
3. Le système crée une **réservation temporaire (HOLD)**, marque les ressources comme *tenues*, et renvoie un **identifiant** + une **échéance d'expiration**.
4. L'interface affiche la réservation avec un **compte à rebours**.

**Variantes / erreurs**
- Une ressource (au moins) n'est plus disponible → ↩️ **409** « Ressource indisponible » et **aucune** réservation créée (tout-ou-rien).
- Requête sans ressource / doublons → ↩️ **400**.
- Non authentifié → ↩️ **401**.

**Contraintes**
- ⚖️ **Atomicité tout-ou-rien** : soit toutes les ressources sont réservées, soit aucune.
- 🔁 **Sérialisation** des demandes concurrentes sur les mêmes ressources (`SELECT … FOR UPDATE`).
- ⚖️ **Invariant** : au plus **une** réservation active par ressource et période.
- ⏱️ Le hold expire après un **délai configurable** (par défaut **5 minutes**).
- 🔒 Opération réservée aux **utilisateurs authentifiés**.

---

## P5 — Confirmer une réservation

- **Acteur :** Client **connecté**, propriétaire de la réservation.
- **Préconditions :** une réservation en statut **HOLD** non expirée.
- **Déclencheur :** clic sur « Confirmer ».

**Déroulé nominal**
1. Le client confirme sa réservation temporaire.
2. Le système passe la réservation en **CONFIRMED** et les ressources en *réservées*.
3. La ressource disparaît définitivement des disponibilités.

**Variantes / erreurs**
- Hold **expiré** entre-temps → ↩️ **409** « Le hold a expiré. » (l'UI repasse la réservation en EXPIRED).
- Réservation d'un **autre utilisateur** → ↩️ **403**.
- Réservation inexistante → ↩️ **404**.
- Statut déjà CONFIRMED/CANCELLED → ↩️ **409**.

**Contraintes**
- ⏱️ Confirmation possible **uniquement avant l'expiration** du hold.
- 🔒 Seul le **propriétaire** (ou un admin) peut confirmer.
- ⚖️ Transition autorisée uniquement depuis l'état **HOLD**.

---

## P6 — Annuler une réservation

- **Acteur :** Client **connecté**, propriétaire de la réservation.
- **Préconditions :** réservation en statut HOLD ou CONFIRMED.
- **Déclencheur :** clic sur « Annuler ».

**Déroulé nominal**
1. Le client demande l'annulation.
2. Le système libère **atomiquement** les ressources (retour à *disponible*) et passe la réservation en **CANCELLED**.
3. Les ressources réapparaissent dans les disponibilités.

**Variantes / erreurs**
- Réservation d'un **autre utilisateur** → ↩️ **403**.
- Réservation inexistante → ↩️ **404**.
- Déjà annulée ou expirée → ↩️ **409** message explicite.

**Contraintes**
- ⚖️ Libération des ressources **atomique** (pas d'état intermédiaire visible).
- 🔒 Seul le **propriétaire** (ou un admin) peut annuler.

---

## P7 — Suivre ses réservations

- **Acteur :** Client **connecté**.
- **Préconditions :** avoir créé au moins une réservation dans la session.
- **Déclencheur :** ouverture de la section « Mes réservations ».

**Déroulé nominal**
1. Le client visualise ses réservations avec leur **statut** (HOLD / CONFIRMED / CANCELLED / EXPIRED).
2. Pour les holds, un **compte à rebours** indique le temps restant.
3. Les actions Confirmer / Annuler sont proposées selon le statut.

**Contraintes**
- ⏱️ Un hold arrivé à échéance bascule **automatiquement** en EXPIRED côté interface, et les boutons d'action disparaissent.

---

## P8 — Concurrence : deux clients, une même ressource (cas critique)

- **Acteurs :** Client A et Client B, connectés.
- **Préconditions :** une ressource disponible, convoitée par les deux.
- **Déclencheur :** A et B tentent de réserver la **même** ressource quasi simultanément.

**Déroulé**
1. A et B envoient leur demande sur la même ressource.
2. Le système **sérialise** les deux transactions (verrou de ligne).
3. **Un seul** obtient la réservation (↩️ **201**, HOLD).
4. L'autre reçoit ↩️ **409** « Ressource indisponible ».

**Contraintes**
- 🔁 **Aucune surréservation possible**, quelle que soit la simultanéité.
- ⚖️ Garantie renforcée par un **index d'unicité** en base (2ᵉ barrière).
- ↩️ La demande perdante échoue **proprement** (pas d'erreur technique, pas de blocage).

---

## P9 — Expiration automatique d'un hold

- **Acteur :** Système (tâche de fond).
- **Préconditions :** un hold non confirmé dont l'échéance est dépassée.
- **Déclencheur :** cycle périodique du worker.

**Déroulé**
1. Le système détecte les holds expirés.
2. Il passe ces réservations en **EXPIRED** et **libère** les ressources associées.
3. Les ressources redeviennent disponibles pour d'autres clients.

**Contraintes**
- ⏱️ Libération au plus tard **un cycle de balayage après l'échéance** du hold.
- ⚖️ Opération **atomique** et cohérente, exécutable en parallèle sur plusieurs nœuds sans conflit.

---

## Synthèse des codes de réponse

| Situation | Code |
|---|---|
| Succès (lecture / action) | 200 |
| Réservation créée | 201 |
| Annulation effectuée | 204 |
| Entrée invalide | 400 |
| Non authentifié / jeton invalide/expiré | 401 |
| Action sur la réservation d'autrui | 403 |
| Réservation introuvable | 404 |
| Ressource indisponible / conflit d'état / concurrence | 409 |
| Trop de requêtes (rate limiting) | 429 |
