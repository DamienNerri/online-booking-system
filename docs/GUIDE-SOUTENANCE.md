# Guide de soutenance — Système de réservation en ligne distribué

> Document de préparation pour la présentation vidéo (Bloc 5 — Traitements distribués).
> Deux intervenants : **Damien** et **Maxime**. Adaptez les prénoms/rôles si besoin.

## 0. Ce que le jury attend (rappel de la grille)

| Critère | Ce qu'il faut prouver | Où on le montre |
|---|---|---|
| 1.1 Automatiser la mise en place de l'architecture | Déploiement en **une commande** | Démo 2 |
| 1.2 Tests de sécurité + anomalies documentées | Jeu de tests sécurité + rapport | Démo 6 |
| 1.3 Outils de protection adaptés aux risques | JWT, hachage, requêtes paramétrées, rate limiting | Démo 6 |
| 1.4 Documentations techniques et d'usage | README + ARCHITECTURE.md | Démo 7 (mention) |
| 1.5 Protocole de validation + cahier de recette | CAHIER-DE-RECETTE.md rejoué | Démos 3-4-5 |

Axes de soutenance évalués : **qualité de la présentation**, **complexité de la tâche réalisée**, **contribution de chacun**, **interaction avec le jury**. → D'où l'importance que **chacun parle** et maîtrise sa partie.

---

## 1. Répartition des rôles (qui dit quoi)

L'idée : chacun **possède** des parties, mais tout le monde comprend l'ensemble
(le jury peut poser une question à l'un sur la partie de l'autre).

| Partie | Porteur principal | Renfort |
|---|---|---|
| Contexte & problématique | Damien | — |
| Architecture distribuée & choix techniques | Damien | Maxime |
| Atomicité & concurrence (le cœur du sujet) | Damien | Maxime |
| Déploiement automatisé (Docker) | Maxime | Damien |
| Sécurité & tests | Maxime | Damien |
| Interface web (démo utilisateur) | Maxime | Damien |
| Conclusion & bilan | Les deux | — |

---

## 2. Préparation AVANT d'enregistrer (checklist)

À faire une fois, juste avant la première prise :

```powershell
# 1. Repartir d'un état propre (données vierges)
docker compose down -v
docker compose up -d --build

# 2. Attendre ~10s puis vérifier que tout répond
Start-Sleep 10
curl.exe http://localhost:8088/health
```

- [ ] Les 4 conteneurs tournent (`docker compose ps` → postgres + 3 api + nginx).
- [ ] `http://localhost:8088/` s'ouvre dans le navigateur.
- [ ] Deux navigateurs différents prêts (ex. Chrome + Firefox, ou une fenêtre privée) pour la démo de concurrence.
- [ ] Un terminal PowerShell ouvert dans le dossier du projet.
- [ ] Zoom de l'éditeur augmenté (lisibilité du code à l'écran).

---

## 3. Déroulé de la démonstration (script vidéo)

Durée cible : **8 à 12 minutes**. Chaque segment indique **qui parle**, **quoi
montrer**, la **commande**, le **résultat attendu** et une **phrase clé** à dire.

### Démo 1 — Introduction & problématique  *(≈1 min · Damien)*
**Montrer :** le fichier `information document/subject.md` puis un schéma (README / ARCHITECTURE.md).
**À dire :**
> « Le sujet : un système de réservation en ligne distribué. Le vrai défi n'est
> pas de réserver, c'est de garantir qu'une même chambre ne soit **jamais**
> réservée deux fois quand des centaines de clients cliquent en même temps, tout
> en répartissant la charge sur plusieurs serveurs et en survivant à une panne. »

Annoncer le plan : architecture → déploiement → preuves (concurrence, panne, sécurité).

---

### Démo 2 — Déploiement automatisé  *(≈1 min · Maxime)*  → **critère 1.1**
**Montrer :** le terminal.
**Commande :**
```powershell
docker compose down -v
docker compose up -d --build
```
**Résultat attendu :** PostgreSQL + 3 nœuds API + Nginx démarrent ; migrations et
jeu de données appliqués automatiquement.
**À dire :**
> « Toute l'infrastructure se lance en une seule commande. Aucune étape manuelle :
> la base, les 3 nœuds applicatifs, le répartiteur de charge, les migrations et
> les données de départ. C'est de l'Infrastructure as Code. »

**Puis prouver la répartition sur plusieurs nœuds :**
```powershell
1..6 | ForEach-Object { (Invoke-RestMethod http://localhost:8088/health).node }
```
**Résultat attendu :** plusieurs identifiants de nœuds différents s'affichent.
**Phrase clé :** « Les requêtes sont bien réparties par Nginx sur les 3 nœuds. »

---

### Démo 3 — Parcours utilisateur (interface web)  *(≈2 min · Maxime)*
**Montrer :** le navigateur sur `http://localhost:8088/`.
**Étapes à filmer :**
1. Créer un compte (email + mot de passe) → connexion.
2. Rechercher des chambres (type = Chambre d'hôtel, du 2026-01-01 au 2026-01-08).
3. Sélectionner une chambre → « Réserver la sélection » → une réservation **HOLD**
   apparaît avec un **compte à rebours**.
4. Cliquer « Confirmer » → passe en **CONFIRMED**.
5. Relancer la recherche → la chambre a disparu des disponibilités.
**À dire :**
> « Une réservation passe d'abord en *hold* temporaire — le temps de confirmer —
> puis en confirmée. Tant qu'elle est tenue, la ressource est bloquée pour les autres. »

---

### Démo 4 — LE cas central : anti-surréservation sous concurrence  *(≈2 min · Damien)*  → **critère 1.5 / cœur du sujet**

**Option A — visuelle (2 navigateurs), la plus parlante :**
1. Navigateur 1 (compte A) : sélectionne une chambre, **ne clique pas encore**.
2. Navigateur 2 (compte B) : sélectionne la **même** chambre.
3. Navigateur 1 : « Réserver » → succès (HOLD).
4. Navigateur 2 : « Réserver » → message **« ressource indisponible »**.
**Phrase clé :** « Un seul gagne. Le second est refusé proprement. »

**Option B — la preuve chiffrée (script), à enchaîner :**
```powershell
powershell -File infra\concurrency-test.ps1
```
**Résultat attendu :** `succes(201)=1  conflit(409)=29` sur 30 requêtes simultanées.
**À dire (technique, montrer le code `BookingRepository.ReserveAsync`) :**
> « 30 clients tentent la même chambre exactement en même temps. La base
> verrouille la ligne avec `SELECT … FOR UPDATE` : les transactions sont
> **sérialisées**. Un seul obtient la réservation, les 29 autres reçoivent un
> conflit. Et même en cas de course résiduelle, un **index unique** empêche
> physiquement la double réservation. C'est notre garantie d'atomicité et de
> cohérence. »

---

### Démo 5 — Tolérance aux pannes  *(≈1,5 min · Damien)*  → **distribution / robustesse**
**Commande :**
```powershell
powershell -File infra\fault-tolerance-test.ps1
```
**Résultat attendu :** on tue un nœud (`docker kill … api-2`), le service continue
(20/20 requêtes OK), une réservation reste possible et cohérente, puis le nœud
réintègre le cluster.
**À dire :**
> « On coupe brutalement un des trois serveurs en plein fonctionnement. Le service
> ne s'arrête pas : Nginx route vers les nœuds restants, et comme ils sont
> *stateless* — tout l'état est en base — aucune réservation n'est perdue ni
> laissée à moitié. Le nœud redémarré rejoint ensuite le cluster. »

---

### Démo 6 — Sécurité  *(≈1,5 min · Maxime)*  → **critères 1.2 et 1.3**
**Commande :**
```powershell
powershell -File infra\security-test.ps1
```
**Résultat attendu :** injection SQL neutralisée · 401 sans jeton · 401 jeton invalide ·
403 accès à la réservation d'autrui · 429 sous rafale (rate limiting).
**À dire :**
> « On a testé les risques classiques : l'injection SQL est neutralisée par des
> requêtes paramétrées, l'accès sans jeton est refusé (401), on ne peut pas
> toucher la réservation d'un autre (403), et une rafale de requêtes est freinée
> (429). Les mots de passe sont hachés en base, jamais en clair. »
**Bonus :** montrer dans `docs/CAHIER-DE-RECETTE.md` le tableau des anomalies et préconisations.

---

### Démo 7 — Documentation & bilan  *(≈1 min · les deux)*  → **critère 1.4**
**Montrer :** `README.md`, `docs/ARCHITECTURE.md`, `docs/CAHIER-DE-RECETTE.md`, et le dossier `.kiro/specs/` (démarche spec-driven : requirements → design → tasks).
**À dire (Damien) :**
> « Toute la démarche est documentée : les exigences au format EARS, le design
> avec les propriétés de correction, et un cahier de recette qui trace chaque test
> à une exigence. »
**À dire (Maxime) :**
> « Bilan : atomicité garantie, aucune surréservation prouvée sous 30 accès
> concurrents, tolérance à la panne d'un nœud, sécurité testée, et déploiement en
> une commande. »

---

## 4. Notions techniques à savoir expliquer (si le jury creuse)

Chacun doit pouvoir répondre sur ces points :

- **Pourquoi PostgreSQL fait la coordination ?** Plutôt qu'un algorithme de
  consensus maison (complexe, risqué), on délègue l'atomicité aux transactions
  ACID. La dimension distribuée vient des **nœuds applicatifs multiples** qui
  s'appuient sur ce point de coordination.
- **`SELECT … FOR UPDATE` :** verrou de ligne. La 2ᵉ transaction sur la même
  chambre **attend**, puis constate l'indisponibilité et échoue → pas de conflit.
- **Index unique partiel** (`booking_slots(slot_id) WHERE active`) : 2ᵉ barrière,
  garantit l'invariant même si le verrou était contourné.
- **Stateless :** aucun état en mémoire d'un nœud ; tout est en base. C'est ce qui
  permet la répartition de charge ET la tolérance aux pannes.
- **Hold + expiration :** une réservation non confirmée expire (TTL) et libère la
  ressource ; un worker périodique s'en charge via une requête atomique.
- **Migrations concurrentes :** un verrou consultatif fait qu'un seul nœud migre au
  démarrage, les autres attendent.

---

## 5. Questions probables du jury + réponses

| Question | Réponse courte |
|---|---|
| « Que se passe-t-il si deux clics arrivent à la même milliseconde ? » | Le `FOR UPDATE` sérialise ; un seul passe. Prouvé par le test de concurrence (1/30). |
| « Et si la base tombe ? » | C'est le point unique à protéger : en prod on met PostgreSQL en réplication (primaire/secondaire). Ici on montre la tolérance côté nœuds applicatifs. |
| « Comment ça scale ? » | On ajoute des réplicas API (`deploy.replicas`) ; ils sont stateless, Nginx répartit. La base reste le point de contention, d'où le verrouillage fin par ligne. |
| « La sécurité en production ? » | Ajouter TLS au répartiteur, secrets dans un coffre (pas dans le compose), rate limiting partagé via Redis. |
| « Pourquoi pas un ORM ? » | On voulait garder la maîtrise explicite des transactions et du `FOR UPDATE`. |

---

## 6. Répartition du temps de parole (équilibre pour la note « contribution »)

| Intervenant | Segments | ~Temps |
|---|---|---|
| Damien | Démos 1, 4, 5 + moitié 7 | ~5 min |
| Maxime | Démos 2, 3, 6 + moitié 7 | ~5 min |

Objectif : **temps de parole équilibré** et **chacun sur des parties techniques**
(pas un qui parle, l'autre qui clique).

---

## 7. Commandes de secours (si un test échoue en direct)

```powershell
docker compose ps                 # état des conteneurs
docker compose logs api           # logs applicatifs
docker compose restart api        # redémarrer les nœuds
docker compose down -v; docker compose up -d --build   # repartir de zéro
```

Astuce : **enregistrez les démos de scripts une fois validées** ; gardez la version
qui passe. Rejouez `concurrency-test.ps1` et `fault-tolerance-test.ps1` juste avant
l'enregistrement pour être sûr de l'état.
