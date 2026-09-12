# Livrables — Architecture Logicielle (Heuristiques et Compromis)

**Projet :** Plateforme web de réservation d'hôtel
**Module :** 5ESGI — Architecture Logicielle : Heuristiques et Compromis

Ce dossier regroupe les livrables demandés par le sujet, rédigés sur le domaine
**hôtel** (sujet retenu et autorisé pour ce projet) et ancrés sur le cours
(heuristiques, compromis, ADR/ATAM/QOC, RGAA 4 / WCAG, typologie de tests).

## Contenu

| # | Livrable | Fichier |
|---|---|---|
| 1 | **Dossier d'architecture logicielle** (livrable principal) : besoins, parcours, choix d'architecture avec heuristiques et compromis, **diagrammes Mermaid** (C4 conteneurs, déploiement, cas d'usage, ER, machines à états, séquence de concurrence), justification, ADR | [`1-dossier-architecture.md`](1-dossier-architecture.md) |
| 2 | **Rapport d'analyse de performance** : scénarios critiques, mesures réelles, goulots, optimisations priorisées, protocole vers 500 utilisateurs | [`2-rapport-performance.md`](2-rapport-performance.md) |
| 3 | **Rapport d'accessibilité RGAA 4** : **audit outillé WAVE** (captures dans `img/`), audit manuel de l'écran clé, non-conformités + corrections, plan d'accessibilité (diagrammes Mermaid) | [`3-rapport-accessibilite-rgaa4.md`](3-rapport-accessibilite-rgaa4.md) |
| 4 | **Support de soutenance** : plan minuté, slides, script de démo, Q&R | [`4-soutenance.md`](4-soutenance.md) |
| — | **Journal des décisions d'architecture (ADR)** | [`adr/ADR.md`](adr/ADR.md) |

Le **prototype** (livrable optionnel) est l'application elle-même :
`http://localhost:8088/` après `docker compose up -d --build`.

## Correspondance avec les livrables exigés

1. Dossier d'architecture logicielle (principal) → Livrable 1 (+ ADR).
2. Rapport d'analyse de performance → Livrable 2.
3. Rapport d'accessibilité RGAA 4 → Livrable 3.
4. Présentation de soutenance → Livrable 4.
5. Maquette / prototype (optionnel) → application fonctionnelle (front + API).

## Documents source réutilisés

- Architecture technique détaillée : `../docs/ARCHITECTURE.md`
- Cartographie des tensions non fonctionnelles : `../rendu/tensions-non-fonctionnelles.md`
- Besoins fonctionnels / non fonctionnels : `../rendu/`
- Cahier de recette : `../docs/CAHIER-DE-RECETTE.md`

## Note sur le domaine et l'implémentation

L'implémentation de référence repose sur un **moteur de réservation générique**
(`resource_slot` = ressource × période). Appliqué à l'hôtel, un slot représente
une **chambre pour une nuit**. Ce choix (« design for change », ADR-006/002)
permet au même socle de servir d'autres inventaires sans refonte, et n'affecte
pas l'invariant central : **aucune surréservation**, garanti par `FOR UPDATE` +
index unique partiel + transactions ACID.

## Reproduire les preuves

```bash
# Démarrer la stack (API ×3, PostgreSQL, Nginx)
docker compose up -d --build

# Tests (dont concurrence, base PostgreSQL réelle)
dotnet test            # 6/6 attendus (4 concurrence + 2 smoke)

# Front
open http://localhost:8088/
```
