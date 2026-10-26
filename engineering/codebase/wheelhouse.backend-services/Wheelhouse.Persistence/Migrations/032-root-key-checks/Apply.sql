-- ============================================================
-- 032-root-key-checks — the owner's offline copy of the vault's root key, checked on a schedule (decision 67).
--
-- Each row records the owner marking their offline copy verified: they printed its fingerprint on their own machine
-- and it matched the fingerprint of the key in use, which the row keeps (a truncated, labelled SHA-256; never the key).
-- The newest row counts while the key in use keeps that fingerprint. Expand only (decision 65). Idempotent.
-- EF is a pure mapper over this hand-authored schema.
-- ============================================================

CREATE SCHEMA IF NOT EXISTS vault;

CREATE TABLE IF NOT EXISTS vault.vault_root_key_verifications (
    id              uuid        PRIMARY KEY,
    fingerprint     text        NOT NULL,
    verified_by     text        NOT NULL,
    reason          text        NOT NULL,
    verified_at_utc timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_vault_root_key_verifications_at ON vault.vault_root_key_verifications (verified_at_utc);
