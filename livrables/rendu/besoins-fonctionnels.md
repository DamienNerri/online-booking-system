# Besoins fonctionnels

Système de réservation en ligne distribué — Bloc 5 (Traitements distribués).
Ce document décrit **ce que le système doit permettre de faire**, du point de vue
métier, indépendamment des choix techniques.

## Périmètre

Le système couvre la **réservation de chambres d'hôtel**. Une chambre est
réservable **par nuit** : l'unité réservable est donc une **chambre pour une nuit
donnée**. (Le modèle est généralisable à d'autres ressources, mais le périmètre
fonctionnel retenu se concentre sur l'hôtellerie.)

## Acteurs

| Acteur | Description |
|---|---|
| **Client** | Recherche et réserve des chambres d'hôtel. |
| **Administrateur** | Rôle prévu ; peut intervenir sur toutes les réservations. |
| **Système** | Gère l'inventaire des chambres, les états des réservations et l'expiration automatique. |

---

## BF1 — Gestion du compte
- Le système doit permettre à un client de **créer un compte** (email + mot de passe).
- Le système doit permettre à un client de **se connecter** et de rester identifié pendant sa session.
- Seuls les utilisateurs **authentifiés et vérifiés** peuvent réserver, confirmer ou annuler.

## BF2 — Validation de l'adresse email
- À l'inscription, le système doit **envoyer un email de vérification** contenant un lien (ou un code) à usage unique.
- Le compte reste en état **« non vérifié »** tant que l'email n'est pas confirmé.
- Un compte non vérifié peut se connecter mais **ne peut pas réserver**.
- Le lien/code de vérification doit **expirer** après un délai (ex. 24 h) et pouvoir être **renvoyé**.

**États du compte :** `NON_VERIFIÉ → VÉRIFIÉ` (après confirmation de l'email).

## BF3 — Authentification à deux facteurs (MFA)
- Le système doit permettre à un client d'**activer une double authentification**.
- Quand la MFA est active, la connexion se fait en deux étapes : mot de passe **puis** un **code à usage unique** (application d'authentification type TOTP, ou code envoyé par email).
- Le code doit être **temporaire** (ex. valable ~30 s pour un TOTP) et **à usage unique**.
- Le système doit prévoir des **codes de secours** en cas de perte du second facteur.
- La MFA peut être **désactivée** par le client après re-vérification de son identité.

## BF4 — Consultation des disponibilités
- Le client doit pouvoir **rechercher les chambres disponibles** pour une **période** (date d'arrivée et date de départ).
- Le système ne présente que les chambres **réellement libres** sur toute la période (ni tenues, ni réservées).
- Une recherche sans résultat renvoie une **liste vide** (et non une erreur).

## BF5 — Réservation d'une ou plusieurs nuits/chambres
- Le client doit pouvoir **réserver une ou plusieurs chambres/nuits en une seule opération**.
- La réservation est **tout-ou-rien** : si une seule nuit/chambre demandée est indisponible, **rien** n'est réservé.
- Une réservation acceptée reçoit un **identifiant unique** et démarre en **réservation temporaire (HOLD)**.
- **Une même chambre ne peut jamais être réservée deux fois** pour une nuit donnée, même en cas de demandes simultanées : au plus un client l'obtient, les autres sont informés de l'indisponibilité.

## BF6 — Confirmation de la réservation
- Le client doit pouvoir **confirmer** sa réservation temporaire pour la rendre définitive **avant son expiration**.
- À la confirmation, les chambres passent en **réservées**.

## BF7 — Expiration automatique
- Une réservation temporaire **non confirmée expire automatiquement** après un délai configurable (par défaut 5 minutes).
- À l'expiration, les chambres sont **libérées** et redeviennent disponibles, sans intervention manuelle.

## BF8 — Annulation
- Le client doit pouvoir **annuler** une réservation (temporaire ou confirmée) ; les chambres redeviennent immédiatement disponibles.
- Un client ne peut **annuler/modifier que ses propres réservations**.
- L'annulation d'une réservation inexistante ou déjà annulée renvoie une **erreur explicite**.

## BF9 — Suivi de ses réservations
- Le client doit pouvoir **consulter ses réservations**, avec leur statut et, pour les réservations temporaires, le **temps restant avant expiration**.

## BF10 — Interface utilisateur accessible
- Le système fournit une **interface web** couvrant : inscription, vérification d'email, connexion (avec MFA le cas échéant), recherche, réservation, confirmation, annulation et suivi.
- En cas d'échec, l'interface affiche un **message clair** (chambre indisponible, hold expiré, accès refusé, email non vérifié, etc.).
- **Accessibilité (objectif WCAG 2.1 niveau AA) :**
  - Navigation complète **au clavier** (ordre de tabulation logique, focus visible).
  - **Contrastes** de couleurs suffisants (texte/fond) et information **jamais portée par la seule couleur** (ex. le statut a un libellé, pas qu'une pastille).
  - **Libellés** explicites sur tous les champs de formulaire et **messages d'erreur** rattachés à leur champ.
  - Structure sémantique (titres, points de repère) et attributs **ARIA** pour les composants dynamiques (compte à rebours, messages).
  - Compatibilité **lecteurs d'écran** ; textes alternatifs sur les éléments visuels porteurs de sens.
  - Cibles cliquables de taille suffisante ; pas de dépendance à un seul sens (souris, vue).

  > Note : la validation complète WCAG nécessite des tests manuels avec des
  > technologies d'assistance et une revue d'expert accessibilité.

---

## Machine à états

### États d'une réservation

| État | Signification | Transitions possibles |
|---|---|---|
| **HOLD** | Réservation temporaire, en attente de confirmation | → CONFIRMED (confirmation) · → CANCELLED (annulation) · → EXPIRED (délai dépassé) |
| **CONFIRMED** | Réservation définitive | → CANCELLED (annulation) |
| **CANCELLED** | Annulée par le client/admin (état terminal) | — |
| **EXPIRED** | Hold non confirmé à temps (état terminal) | — |

```
                 confirmer
        ┌────────────────────────► CONFIRMED ──annuler──► CANCELLED
 (création)                            
   HOLD ──────annuler──────────────► CANCELLED
     │
     └────────expiration (TTL)──────► EXPIRED
```

### États d'une chambre (par nuit)

| État | Signification | Déclenché par |
|---|---|---|
| **AVAILABLE** | Libre, réservable | état initial ; annulation ; expiration |
| **HELD** | Tenue par un hold en cours | création d'un HOLD |
| **BOOKED** | Réservée définitivement | confirmation |

```
 AVAILABLE ──réserver──► HELD ──confirmer──► BOOKED
     ▲                    │                    │
     │                    │                    │
     └──expiration────────┘                    │
     └──annulation───────────────────────────-┘
```

---

## Règles de gestion clés
- ⚖️ Unité réservable = une **chambre pour une nuit donnée**.
- ⚖️ Une chambre tenue par un hold est **indisponible** pour les autres pendant la durée du hold.
- ⚖️ Durée du hold **configurable** (défaut 5 minutes).
- ⚖️ Réservation impossible tant que l'**email n'est pas vérifié**.
- ⚖️ Si la **MFA** est active, la connexion exige le **second facteur**.
- ⚖️ Un client n'agit que sur **ses propres** réservations.

---

## Traçabilité vers les exigences

| Besoin | Requirements |
|---|---|
| BF1 Compte | 8 |
| BF2 Validation email | 8 (extension) |
| BF3 MFA | 8 (extension) |
| BF4 Disponibilités | 1 |
| BF5 Réservation | 2, 3 |
| BF6 Confirmation | 4.4 |
| BF7 Expiration | 4 |
| BF8 Annulation | 5, 8.3 |
| BF9 Suivi | 14 |
| BF10 Interface accessible | 14 |

> Statut d'implémentation : BF1, BF4-BF9 et l'interface (BF10) sont **réalisés**.
> BF2 (validation email), BF3 (MFA) et le niveau d'accessibilité AA complet sont
> exprimés comme **besoins cibles** (évolutions prévues).
