# Tensions entre besoins non fonctionnels — Cartographie des arbitrages

Système de réservation en ligne distribué — Bloc 5 (Traitements distribués).

Les besoins non fonctionnels ne sont pas indépendants : renforcer l'un affaiblit
souvent un autre. Ce document **identifie les tensions**, les **cartographie**, et
explicite pour chacune l'**arbitrage retenu** avec ses **avantages / inconvénients**
et ses **mitigations**.

---

## 1. Cartographie d'ensemble

```
                         COHÉRENCE FORTE (BNF1/BNF2)
                         zéro surréservation
                                  ▲
                                  │  T1 (verrou = latence)
                   T2 (CAP)       │        T3 (BD = goulot)
        DISPONIBILITÉ ◄───────────┼───────────► SCALABILITÉ / PERFORMANCE
        (BNF3)                    │             (BNF4 / BNF5)
                                  │
                   T4 (friction)  │  T5 (durabilité = coût commit)
                                  ▼
                          SÉCURITÉ (BNF6) · DURABILITÉ (BNF7)
```

| # | Tension | Pôles en conflit | Arbitrage retenu |
|---|---|---|---|
| T1 | Cohérence ⇄ Performance | Verrouillage `FOR UPDATE` vs débit/latence | Cohérence prioritaire, verrou **fin** (par ligne) |
| T2 | Cohérence ⇄ Disponibilité (CAP) | Source de vérité unique vs tolérance au partitionnement | **CP** : on privilégie la cohérence |
| T3 | Atomicité ⇄ Scalabilité | Base = point de coordination vs mise à l'échelle | Nœuds applicatifs scalables, **BD = point de contention assumé** |
| T4 | Sécurité ⇄ Expérience/Perf | MFA, rate limiting vs fluidité | Sécurité prioritaire, friction **maîtrisée** |
| T5 | Durabilité ⇄ Performance | Commit durable vs latence d'écriture | Durabilité prioritaire (données de réservation) |
| T6 | Rate limiting exact ⇄ Simplicité distribuée | Comptage global vs local par nœud | Local (simple) **pour l'instant**, Redis en cible |
| T7 | Verrou pessimiste ⇄ optimiste | Bloquer tôt vs réessayer | **Pessimiste** (`FOR UPDATE`) |
| T8 | Blocage par hold ⇄ Disponibilité de l'inventaire | Réserver une fenêtre vs libérer vite | Hold à **TTL court configurable** |

---

## T1 — Cohérence forte ⇄ Performance / débit

**Le conflit :** garantir zéro surréservation impose de sérialiser les accès à une
même chambre (`SELECT … FOR UPDATE`). Or sérialiser = faire attendre = réduire le
débit et augmenter la latence sous forte charge.

**Arbitrage :** la cohérence prime (une surréservation est inacceptable
métier), mais on limite le coût avec un **verrou par ligne** (pas par table).

| Avantages | Inconvénients |
|---|---|
| Aucune surréservation, garantie forte | Latence accrue en cas de contention sur **la même** chambre |
| Verrou fin : seules les chambres visées sont bloquées | Points chauds possibles (une chambre très demandée) |
| Comportement déterministe et simple à raisonner | Débit borné par la BD sur les écritures conflictuelles |

**Mitigations :** verrou au grain le plus fin possible ; transactions courtes ;
index adaptés ; lecture (disponibilité) non bloquante.

---

## T2 — Cohérence ⇄ Disponibilité (théorème CAP)

**Le conflit :** en système distribué, face à un partitionnement réseau, on ne peut
pas être à la fois totalement cohérent **et** totalement disponible. Notre source
de vérité unique (PostgreSQL) impose un choix.

**Arbitrage : CP** (Cohérence + tolérance au partitionnement). En cas de problème,
on préfère **refuser** une opération plutôt que risquer une double réservation.

| Avantages | Inconvénients |
|---|---|
| Invariant métier toujours respecté | Si la BD est injoignable, les écritures échouent |
| Modèle mental simple, pas de réconciliation | Disponibilité en écriture dépend de la BD |
| Pas d'incohérence à corriger a posteriori | Moins « toujours dispo » qu'un système AP |

**Mitigations :** en production, réplication PostgreSQL (primaire/secondaire) et
bascule automatique pour réduire l'indisponibilité de la BD.

---

## T3 — Atomicité ⇄ Scalabilité horizontale

**Le conflit :** on scale facilement les **nœuds applicatifs** (stateless), mais ils
convergent tous vers **une** base qui coordonne la concurrence. La base devient le
**goulot d'étranglement**.

**Arbitrage :** scalabilité applicative illimitée, **contention concentrée sur la
BD assumée** comme prix de l'atomicité.

| Avantages | Inconvénients |
|---|---|
| Ajout de nœuds API trivial (`replicas`) | La BD reste le facteur limitant en écriture |
| Nœuds interchangeables, sans état | Le sharding (partition des données) complexifierait fortement |
| Simplicité opérationnelle | Scalabilité écriture non linéaire |

**Mitigations :** pool de connexions, index, transactions courtes ; à grande
échelle, partitionnement par hôtel/région ou cache lecture.

---

## T4 — Sécurité ⇄ Expérience utilisateur / Performance

**Le conflit :** MFA, validation d'email, rate limiting et hachage renforcent la
sécurité mais ajoutent de la **friction** (étapes en plus) et de la **latence**
(hachage BCrypt coûteux par nature, vérifications).

**Arbitrage :** sécurité prioritaire, friction **maîtrisée** (MFA optionnelle,
seuils de rate limiting raisonnables).

| Avantages | Inconvénients |
|---|---|
| Comptes et réservations protégés | Étapes supplémentaires (MFA, email) = abandon possible |
| BCrypt résiste au brute force | BCrypt volontairement lent → coût CPU au login |
| Rate limiting freine les abus | Risque de faux positifs (client légitime limité) |

**Mitigations :** MFA activable au choix ; rate limiting calibré ; messages clairs ;
hachage à coût ajustable.

---

## T5 — Durabilité ⇄ Performance d'écriture

**Le conflit :** garantir qu'une réservation confirmée survit à un crash impose un
commit durable (écriture disque), plus lent qu'une écriture en mémoire.

**Arbitrage :** durabilité prioritaire pour les réservations (donnée critique).

| Avantages | Inconvénients |
|---|---|
| Aucune réservation confirmée perdue | Latence de commit plus élevée |
| Récupération après redémarrage | Débit d'écriture borné par le disque |

**Mitigations :** volume persistant ; en prod, disque rapide et réplication.

---

## T6 — Rate limiting exact ⇄ Simplicité en distribué

**Le conflit :** avec plusieurs nœuds, un comptage **exact** par client nécessite un
magasin partagé (Redis). Un comptage **local par nœud** est simple mais approximatif
(un client réparti sur 3 nœuds peut dépasser le seuil global).

**Arbitrage :** **local par nœud** dans cette version (simplicité, aucune dépendance
supplémentaire) ; **Redis** identifié comme évolution.

| Avantages | Inconvénients |
|---|---|
| Zéro dépendance, aucune latence réseau | Seuil global non exact (jusqu'à N×seuil) |
| Simple à déployer et raisonner | Moins efficace derrière un LB round-robin |

**Mitigations :** documenté comme limite ; passage à un compteur partagé (Redis) en cible.

---

## T7 — Verrou pessimiste ⇄ Verrou optimiste

**Le conflit :** pessimiste (`FOR UPDATE`) = bloquer dès la lecture ; optimiste =
laisser faire puis détecter le conflit au commit et réessayer.

**Arbitrage : pessimiste**, car les conflits sur une même chambre sont **fréquents**
en cas de forte demande, et l'échec propre immédiat est préférable au réessai.

| Avantages | Inconvénients |
|---|---|
| Échec immédiat et clair (409) | Verrous détenus le temps de la transaction |
| Pas de logique de retry côté client | Moins bon si les conflits étaient rares |
| Simple à raisonner | Risque théorique de contention si transactions longues |

**Mitigations :** transactions minimales ; l'index unique reste un filet de sécurité.

---

## T8 — Blocage par hold ⇄ Disponibilité de l'inventaire

**Le conflit :** le hold réserve une fenêtre de temps pour laisser le client
confirmer, mais pendant ce temps la chambre est **indisponible** pour les autres —
ce qui peut « geler » de l'inventaire.

**Arbitrage :** hold à **TTL court et configurable** (défaut 5 min), libéré
automatiquement.

| Avantages | Inconvénients |
|---|---|
| Expérience équitable (le premier a le temps de finir) | Inventaire temporairement gelé |
| Libération automatique (worker) | TTL trop long = chambres bloquées inutilement |
| Pas de réservation « fantôme » | TTL trop court = holds qui expirent avant confirmation |

**Mitigations :** TTL ajustable ; compte à rebours visible ; expiration automatique.

---

## 2. Synthèse des arbitrages

| Axe privilégié | Axe sacrifié (partiellement) | Justification métier |
|---|---|---|
| **Cohérence** | Performance brute, disponibilité en écriture | Une surréservation est inacceptable |
| **Sécurité** | Fluidité maximale | Données personnelles + paiement à venir |
| **Durabilité** | Latence d'écriture | Une réservation confirmée est un engagement |
| **Simplicité** | Scalabilité extrême, rate limiting exact | Cadre pédagogique, évolutions identifiées |

**Principe directeur :** en cas de doute, on **refuse proprement** plutôt que de
risquer une incohérence. La correction métier prime sur la performance et la
disponibilité absolue — choix cohérent avec un système de réservation.

## 3. Positionnement sur les grands compromis

- **CAP :** système **CP** (cohérence + tolérance au partitionnement).
- **PACELC :** en fonctionnement normal (Else), on privilégie la **cohérence** sur la latence (EC).
- **Verrouillage :** **pessimiste** plutôt qu'optimiste.
- **État :** **stateless** applicatif + état centralisé en base.
