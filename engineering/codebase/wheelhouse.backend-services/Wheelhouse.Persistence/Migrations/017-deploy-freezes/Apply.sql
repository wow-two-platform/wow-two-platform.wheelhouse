-- ============================================================
-- 017-deploy-freezes — holds on deploys. A freeze covers everything (product_id and environment null), one product
-- (environment null) or one product's environment (both set); an environment without a product is refused. While a
-- freeze covers a target, automatic deploys skip it and an owner's deploy needs an override with a reason. One freeze
-- per scope (NULLS NOT DISTINCT, PostgreSQL 15+); a product's freezes go with it. actor names who froze it.
-- Idempotent. EF is a pure mapper over this hand-authored schema (schema-first) — see persistence/database.md.
-- ============================================================

CREATE TABLE IF NOT EXISTS deploy_freezes (
    id             uuid        PRIMARY KEY,
    product_id     uuid        NULL REFERENCES products (id) ON DELETE CASCADE,
    environment    text        NULL,
    reason         text        NULL,
    actor          text        NOT NULL,
    created_at_utc timestamptz NOT NULL,
    CONSTRAINT ck_deploy_freezes_scope CHECK (environment IS NULL OR product_id IS NOT NULL)
);
CREATE UNIQUE INDEX IF NOT EXISTS ix_deploy_freezes_scope ON deploy_freezes (product_id, environment) NULLS NOT DISTINCT;
