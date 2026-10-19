-- ============================================================
-- 026-deployment-intents — deploys that changed nothing because they never reached their target: refused before
-- submission (outcome refused, refusal gate | freeze | validation), replaced in the queue by a newer release
-- (superseded, replaced_by names it), cancelled, expired or aborted. The runner never hears of them, so the control
-- plane keeps them and the deployment history joins them as Not applied deploys. Targets and products are slugs, as
-- the runner's records name them, so a row outlives its target. cause is the automation's reason (auto_dev |
-- auto_promote), null for an owner's deploy. Idempotent. EF is a pure mapper over this hand-authored schema
-- (schema-first) — see persistence/database.md.
-- ============================================================

CREATE TABLE IF NOT EXISTS deployment_intents (
    id          uuid        PRIMARY KEY,
    target      text        NOT NULL,
    product     text        NULL,
    environment text        NULL,
    bundle_id   text        NULL,
    release     text        NULL,
    actor       text        NOT NULL,
    outcome     text        NOT NULL,
    refusal     text        NULL,
    message     text        NULL,
    replaced_by text        NULL,
    cause       text        NULL,
    fleet_id    uuid        NULL,
    at_utc      timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_deployment_intents_at ON deployment_intents (at_utc);
