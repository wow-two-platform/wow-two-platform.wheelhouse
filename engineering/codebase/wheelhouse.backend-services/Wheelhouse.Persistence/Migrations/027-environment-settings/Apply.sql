-- ============================================================
-- 027-environment-settings — products' non-secret setting values per environment, keyed by configuration path
-- (DataProtection:ApplicationName). Each deploy renders them into the settings file of every service that declares the
-- key, or of every service that takes a settings file when none declares it; secret values stay in the vault. The
-- first-deploy prompt and the settings editor write them, audited without values. Rows go with their product.
-- Idempotent. EF is a pure mapper over this hand-authored schema (schema-first) — see persistence/database.md.
-- ============================================================

CREATE TABLE IF NOT EXISTS environment_settings (
    product_id     uuid        NOT NULL REFERENCES products (id) ON DELETE CASCADE,
    environment    text        NOT NULL,
    key            text        NOT NULL,
    value          text        NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    PRIMARY KEY (product_id, environment, key)
);
