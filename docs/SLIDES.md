# Plan des slides — Soutenance

> Support conseillé : **16 slides** (+ 3 slides « DÉMO » qui servent de transition
> vers les captures/vidéos live). Durée cible 8-12 min. Chaque slide indique :
> le **titre**, le **contenu à afficher** (peu de texte !), le **visuel**, **qui
> présente**, les **notes orateur** (ce qu'on dit) et le **critère de grille**.
>
> Règle d'or : la slide affiche des mots-clés, l'orateur développe à l'oral.

---

## Slide 1 — Page de titre
- **Contenu :** Titre « Système de réservation en ligne distribué » · sous-titre
  « Bloc 5 — Traitements distribués » · noms (Damien, Maxime) · date · logo école.
- **Visuel :** fond sobre, éventuellement le schéma d'archi en filigrane.
- **Présente :** Damien.
- **Notes :** « Bonjour, nous allons vous présenter notre système de réservation
  distribué. » (10 s, on enchaîne).

## Slide 2 — Sommaire
- **Contenu :** 5 points : Problématique · Architecture · Le défi de la concurrence ·
  Robustesse & sécurité · Démarche et bilan.
- **Présente :** Damien.
- **Notes :** annoncer le fil rouge : « du problème métier jusqu'aux preuves techniques ».

## Slide 3 — Contexte & problématique  → *cadrage*
- **Contenu :** le sujet (réserver hôtels/vols) + LA question centrale en gros :
  « Comment garantir qu'une chambre ne soit **jamais réservée deux fois** quand
  des centaines de clients réservent en même temps, sur plusieurs serveurs ? »
- **Visuel :** illustration 2 clients → 1 chambre.
- **Présente :** Damien.
- **Notes :** insister : le vrai défi n'est pas de réserver, c'est la **concurrence**,
  la **cohérence** et la **disponibilité** en distribué.

## Slide 4 — Objectifs & exigences
- **Contenu :** 4 garanties visées : Atomicité · Zéro surréservation ·
  Tolérance aux pannes · Sécurité. + mention « 14 exigences formalisées (EARS) ».
- **Visuel :** 4 icônes.
- **Présente :** Damien.
- **Notes :** « On a d'abord formalisé les exigences avant de coder » (démarche sérieuse).

## Slide 5 — Architecture globale  → *cœur technique*
- **Contenu :** le schéma :
  `Clients → Nginx (répartiteur) → 3 nœuds API stateless → PostgreSQL`.
- **Visuel :** diagramme (repris de `docs/ARCHITECTURE.md`).
- **Présente :** Damien (renfort Maxime).
- **Notes :** expliquer chaque étage : répartition de charge, nœuds interchangeables,
  base = source de vérité unique.
- **Grille : distribution.**

## Slide 6 — Stack technique & choix
- **Contenu :** tableau : ASP.NET Core 8 · PostgreSQL 16 · Nginx · Docker Compose.
  Une phrase de justification par élément.
- **Présente :** Maxime.
- **Notes :** « Pas d'ORM : on garde la maîtrise explicite des transactions et du
  verrouillage. »

## Slide 7 — Modèle de données & l'invariant
- **Contenu :** 3 tables (`resource_slots`, `bookings`, `booking_slots`) +
  encadré rouge : « **Index unique** → au plus 1 réservation active par slot ».
- **Visuel :** mini-schéma relationnel.
- **Présente :** Damien.
- **Notes :** c'est ici que vit la garantie anti-surréservation, au niveau de la base.
- **Grille : 1.5 (conception).**

## Slide 8 — Le cœur : atomicité & concurrence
- **Contenu :** le pseudo-flux :
  `BEGIN → SELECT … FOR UPDATE → vérifier dispo → INSERT/UPDATE → COMMIT`.
  Deux idées clés : **verrou de ligne** (sérialisation) + **index unique** (2ᵉ barrière).
- **Visuel :** schéma 2 transactions, l'une attend l'autre.
- **Présente :** Damien.
- **Notes :** « La 2ᵉ transaction attend, puis échoue proprement. Défense en profondeur. »
- **Grille : cœur du sujet.**

## Slide 9 — DÉMO 1 : concurrence  🎬
- **Contenu :** titre « Démonstration : 30 clients, 1 chambre » + résultat attendu
  affiché : `succès = 1 · conflits = 29`.
- **Action :** lancer `infra\concurrency-test.ps1` (ou la démo 2 navigateurs).
- **Présente :** Damien.
- **Notes :** commenter le résultat en direct : un seul gagne, c'est prouvé.
- **Grille : 1.5.**

## Slide 10 — Distribution & tolérance aux pannes
- **Contenu :** 2 points : nœuds **stateless** (état 100 % en base) · Nginx route
  autour d'un nœud mort. Schéma « on coupe api-2, le service continue ».
- **Présente :** Damien.
- **Notes :** relier statelessness ↔ scalabilité ↔ tolérance aux pannes.
- **Grille : distribution / robustesse.**

## Slide 11 — DÉMO 2 : panne d'un nœud  🎬
- **Contenu :** titre « On tue un serveur en pleine activité » + attendu :
  `20/20 requêtes OK · réservation cohérente · nœud réintégré`.
- **Action :** lancer `infra\fault-tolerance-test.ps1`.
- **Présente :** Damien.
- **Notes :** insister : aucune réservation perdue ni à moitié.

## Slide 12 — Sécurité
- **Contenu :** 4 protections : JWT (auth) · mots de passe hachés (BCrypt) ·
  requêtes paramétrées (anti-injection) · rate limiting. + « ownership : 403 ».
- **Présente :** Maxime.
- **Notes :** relier chaque protection à un risque.
- **Grille : 1.2 et 1.3.**

## Slide 13 — DÉMO 3 : tests de sécurité  🎬
- **Contenu :** titre + attendu : `injection neutralisée · 401 · 403 · 429`.
- **Action :** lancer `infra\security-test.ps1` (+ montrer le tableau d'anomalies du cahier de recette).
- **Présente :** Maxime.
- **Grille : 1.2, 1.3.**

## Slide 14 — Déploiement automatisé (IaC)
- **Contenu :** une seule commande : `docker compose up -d --build`. Liste des
  services provisionnés. Mention : migrations + données au démarrage.
- **Visuel :** capture du terminal (4 conteneurs up).
- **Présente :** Maxime.
- **Notes :** « Reproductible sur une machine propre, sans étape manuelle. »
- **Grille : 1.1.**

## Slide 15 — Démarche & qualité
- **Contenu :** spec-driven (Requirements EARS → Design → Tasks) · tests
  (unitaires + concurrence + panne + sécurité) · cahier de recette tracé aux exigences.
- **Visuel :** mini-capture de l'arbo `.kiro/specs/` + `docs/`.
- **Présente :** Damien + Maxime (une phrase chacun).
- **Grille : 1.4, 1.5.**

## Slide 16 — Bilan, limites & perspectives
- **Contenu :** ce qui est prouvé (atomicité, 0 surréservation, tolérance panne,
  sécurité, 1 commande) · limites (base = point unique → réplication en prod ;
  rate limiting local → Redis ; TLS) · perspectives.
- **Présente :** les deux.
- **Notes :** finir sur l'honnêteté technique : on connaît nos limites et comment les lever.

## Slide 17 — Merci / Questions
- **Contenu :** « Merci — Questions ? » + lien GitHub + noms.
- **Présente :** les deux.

---

## Récap : répartition & timing

| Slides | Intervenant | Thème | ~Temps |
|---|---|---|---|
| 1-5, 7-11 | Damien | Problématique, archi, concurrence, pannes | ~5 min |
| 6, 12-14 | Maxime | Stack, sécurité, déploiement | ~4 min |
| 15-17 | Les deux | Démarche, bilan, questions | ~2 min |

## Conseils de mise en forme
- **Peu de texte par slide** (3-5 puces max), gros titres, un visuel par slide.
- **Cohérence graphique** : mêmes couleurs que le front (fond sombre + accent bleu).
- **Les slides DÉMO** ne contiennent que le titre + le résultat attendu : le reste
  se passe à l'écran (terminal / navigateur / vidéo).
- **Numérotez** les slides et gardez le schéma d'architecture visible en fil rouge.
- Enregistrez les démos **une fois validées** et intégrez-les si le live est risqué.
