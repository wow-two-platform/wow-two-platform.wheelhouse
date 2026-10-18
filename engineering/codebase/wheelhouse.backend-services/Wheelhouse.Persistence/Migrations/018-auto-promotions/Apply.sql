-- ============================================================
-- 018-auto-promotions — the release auto-promote last tried on each prod target, so a promotion the target or a gate
-- refuses is tried once, never again every pass. A newer release that passes its dev watch replaces the mark. Goes
-- with its target. Idempotent. EF is a pure mapper over this hand-authored schema (schema-first).
-- ============================================================

CREATE TABLE IF NOT EXISTS auto_promotions (
    target_id        uuid        PRIMARY KEY REFERENCES targets (id) ON DELETE CASCADE,
    release          text        NOT NULL,
    attempted_at_utc timestamptz NOT NULL
);
