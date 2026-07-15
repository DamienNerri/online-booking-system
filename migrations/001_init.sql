-- Migration 001 — schéma initial du système de réservation distribué
-- Source de vérité et point de coordination de la concurrence (Req 2, 3, 6, 7).

CREATE TABLE IF NOT EXISTS users (
    id            BIGSERIAL PRIMARY KEY,
    email         TEXT NOT NULL UNIQUE,
    password_hash TEXT NOT NULL,
    role          TEXT NOT NULL DEFAULT 'CUSTOMER', -- CUSTOMER | ADMIN
    created_at    TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- Unité réservable atomique : une ressource pour une période donnée.
CREATE TABLE IF NOT EXISTS resource_slots (
    id            BIGSERIAL PRIMARY KEY,
    resource_id   BIGINT NOT NULL,
    resource_type TEXT   NOT NULL,               -- HOTEL_ROOM | FLIGHT_SEAT | ...
    period_start  DATE   NOT NULL,
    period_end    DATE   NOT NULL,
    status        TEXT   NOT NULL DEFAULT 'AVAILABLE', -- AVAILABLE | HELD | BOOKED
    -- Invariant d'unicité d'une unité de ressource sur une période (Req 3.3)
    CONSTRAINT uq_slot_resource_period UNIQUE (resource_id, period_start, period_end),
    CONSTRAINT ck_slot_period CHECK (period_end > period_start),
    CONSTRAINT ck_slot_status CHECK (status IN ('AVAILABLE', 'HELD', 'BOOKED'))
);

CREATE INDEX IF NOT EXISTS ix_slots_search
    ON resource_slots (resource_type, status, period_start, period_end);

CREATE TABLE IF NOT EXISTS bookings (
    id         BIGSERIAL PRIMARY KEY,
    user_id    BIGINT NOT NULL REFERENCES users (id),
    status     TEXT   NOT NULL,                   -- HOLD | CONFIRMED | CANCELLED | EXPIRED
    expires_at TIMESTAMPTZ,                       -- échéance du hold (Req 4)
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT ck_booking_status CHECK (status IN ('HOLD', 'CONFIRMED', 'CANCELLED', 'EXPIRED'))
);

CREATE INDEX IF NOT EXISTS ix_bookings_expiry
    ON bookings (status, expires_at);

-- Association réservation <-> slots. La contrainte UNIQUE(slot_id) partielle
-- garantit qu'un slot n'appartient qu'à AU PLUS UNE réservation active
-- (HOLD ou CONFIRMED) : c'est l'invariant anti-surréservation (Req 3.3, Property 2).
CREATE TABLE IF NOT EXISTS booking_slots (
    id         BIGSERIAL PRIMARY KEY,
    booking_id BIGINT NOT NULL REFERENCES bookings (id) ON DELETE CASCADE,
    slot_id    BIGINT NOT NULL REFERENCES resource_slots (id),
    active     BOOLEAN NOT NULL DEFAULT TRUE
);

-- Un slot ne peut être lié qu'à une seule réservation active à la fois.
CREATE UNIQUE INDEX IF NOT EXISTS uq_active_slot
    ON booking_slots (slot_id)
    WHERE active = TRUE;
