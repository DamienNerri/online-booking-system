-- Migration 004 — Vues analytics anonymisées (Contrainte C6)
-- Agrège les données de réservation SANS exposer d'informations personnelles.
-- Aucun user_id, aucun email dans les résultats.

CREATE OR REPLACE VIEW v_analytics_bookings_by_room AS
SELECT
    rs.resource_id,
    rs.resource_type,
    COUNT(DISTINCT b.id)                                           AS total_bookings,
    COUNT(DISTINCT CASE WHEN b.status = 'CONFIRMED' THEN b.id END) AS confirmed_bookings,
    COUNT(DISTINCT CASE WHEN b.status = 'CANCELLED' THEN b.id END) AS cancelled_bookings,
    COUNT(DISTINCT CASE WHEN b.status = 'EXPIRED'   THEN b.id END) AS expired_bookings,
    ROUND(
        COUNT(DISTINCT CASE WHEN b.status = 'CANCELLED' THEN b.id END)::numeric
        / NULLIF(COUNT(DISTINCT b.id), 0) * 100, 1
    )                                                              AS cancellation_rate_pct
FROM bookings b
JOIN booking_slots bs ON bs.booking_id = b.id
JOIN resource_slots rs ON rs.id = bs.slot_id
GROUP BY rs.resource_id, rs.resource_type;

CREATE OR REPLACE VIEW v_analytics_bookings_by_week AS
SELECT
    DATE_TRUNC('week', b.created_at)::date AS week_start,
    COUNT(*)                               AS total_bookings,
    COUNT(CASE WHEN b.status = 'CONFIRMED' THEN 1 END) AS confirmed,
    COUNT(CASE WHEN b.status = 'CANCELLED' THEN 1 END) AS cancelled
FROM bookings b
GROUP BY DATE_TRUNC('week', b.created_at)
ORDER BY week_start DESC;
