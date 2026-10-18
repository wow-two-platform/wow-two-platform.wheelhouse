-- ============================================================
-- 016-deploy-automation — what the control plane deploys on its own.
-- product_automation: one row per product that differs from the defaults (auto-deploy each new release to dev ON,
--   auto-promote to prod OFF); a product without a row takes the defaults, and its row goes with it. Wheelhouse
--   itself is CI-deployed (deploy: ci in the wow2 catalog), so its row starts with auto-deploy off.
-- release_watermarks: per product, the newest published release the release watcher has seen; a release newer than
--   it is new. The first read of a product only records the watermark, so turning the watcher on never deploys an
--   existing backlog.
-- deployment_queue: one waiting automatic deploy per target, latest wins; a newer release replaces a queued older one,
--   and a queued deploy waits for the target's running rollout to finish. Goes with its target.
-- Idempotent. EF is a pure mapper over this hand-authored schema (schema-first) — see persistence/database.md.
-- ============================================================

CREATE TABLE IF NOT EXISTS product_automation (
    product_id      uuid        PRIMARY KEY REFERENCES products (id) ON DELETE CASCADE,
    auto_deploy_dev boolean     NOT NULL DEFAULT true,
    auto_promote    boolean     NOT NULL DEFAULT false,
    created_at_utc  timestamptz NOT NULL,
    updated_at_utc  timestamptz NOT NULL
);

INSERT INTO product_automation (product_id, auto_deploy_dev, auto_promote, created_at_utc, updated_at_utc)
SELECT id, false, false, now(), now() FROM products WHERE slug = 'wheelhouse'
ON CONFLICT (product_id) DO NOTHING;

CREATE TABLE IF NOT EXISTS release_watermarks (
    product_id      uuid        PRIMARY KEY REFERENCES products (id) ON DELETE CASCADE,
    release         text        NULL,
    published_at    timestamptz NULL,
    checked_at_utc  timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS deployment_queue (
    target_id       uuid        PRIMARY KEY REFERENCES targets (id) ON DELETE CASCADE,
    bundle_id       text        NOT NULL,
    release         text        NOT NULL,
    published_at    timestamptz NULL,
    cause           text        NOT NULL,
    queued_at_utc   timestamptz NOT NULL
);
