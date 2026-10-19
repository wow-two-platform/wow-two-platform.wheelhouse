-- ============================================================
-- 022-fleet-rollouts — several products promoted to prod in one run, in order, stopping at the first failure.
-- fleet_rollouts: one row per fleet — who started it, the reason and freeze override every deploy carries, where it
--   stands (running | succeeded | failed | cancelled), a cancel request and when it ended. One runs at a time (partial
--   unique index).
-- fleet_rollout_items: its products in order — product and prod target slugs (kept, so a fleet outlives a removed
--   target), the bundle and release, whether the target ID was typed, where each stands (waiting | running |
--   succeeded | failed | refused | not_started), its deployment and a display-safe detail. Goes with its fleet.
-- Idempotent. EF is a pure mapper over this hand-authored schema (schema-first) — see persistence/database.md.
-- ============================================================

CREATE TABLE IF NOT EXISTS fleet_rollouts (
    id                      uuid        PRIMARY KEY,
    actor                   text        NOT NULL,
    reason                  text        NULL,
    override_freeze         boolean     NOT NULL DEFAULT false,
    status                  text        NOT NULL,
    cancel_requested_by     text        NULL,
    cancel_requested_at_utc timestamptz NULL,
    created_at_utc          timestamptz NOT NULL,
    completed_at_utc        timestamptz NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ix_fleet_rollouts_one_running ON fleet_rollouts ((true)) WHERE status = 'running';
CREATE INDEX IF NOT EXISTS ix_fleet_rollouts_created ON fleet_rollouts (created_at_utc DESC);

CREATE TABLE IF NOT EXISTS fleet_rollout_items (
    fleet_id         uuid        NOT NULL REFERENCES fleet_rollouts (id) ON DELETE CASCADE,
    position         integer     NOT NULL,
    product          text        NOT NULL,
    target           text        NOT NULL,
    bundle_id        text        NOT NULL,
    release          text        NULL,
    from_release     text        NULL,
    confirmed        boolean     NOT NULL DEFAULT false,
    status           text        NOT NULL,
    deployment_id    text        NULL,
    detail           text        NULL,
    started_at_utc   timestamptz NULL,
    completed_at_utc timestamptz NULL,
    PRIMARY KEY (fleet_id, position)
);
