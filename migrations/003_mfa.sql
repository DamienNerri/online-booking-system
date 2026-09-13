-- Migration 003 — Authentification MFA TOTP (BF3)
-- Ajoute le support TOTP (Time-based One-Time Password, RFC 6238)
-- sans casser les comptes existants (colonnes nullable / default false).

ALTER TABLE users
    ADD COLUMN IF NOT EXISTS mfa_secret  TEXT,
    ADD COLUMN IF NOT EXISTS mfa_enabled BOOLEAN NOT NULL DEFAULT FALSE;
