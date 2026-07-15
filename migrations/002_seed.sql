-- Migration 002 — jeu de données de démonstration (idempotent).
-- Génère des slots "chambre d'hôtel" pour 20 chambres sur 30 nuits.

INSERT INTO resource_slots (resource_id, resource_type, period_start, period_end)
SELECT
    room.n AS resource_id,
    'HOTEL_ROOM' AS resource_type,
    (DATE '2026-01-01' + night.n) AS period_start,
    (DATE '2026-01-01' + night.n + 1) AS period_end
FROM generate_series(1, 20) AS room(n)
CROSS JOIN generate_series(0, 29) AS night(n)
ON CONFLICT (resource_id, period_start, period_end) DO NOTHING;
