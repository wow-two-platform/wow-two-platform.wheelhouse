-- ============================================================
-- 009-server-vitals — a server's own host readings beside its targets' container readings.
-- A server reads its host itself, so one that runs no target yet still has vitals and a trend. Its row carries no
-- target: target_id becomes nullable, and a NULL target_id marks the server's host figures (containers stay 0).
-- Target rows keep only container health from now on.
-- Backfill: earlier passes read each host through its targets, so each server and pass lifts the host figures of
-- one target row (the first by target, preferring a readable row with figures) into a server row, keeping the
-- trends the servers page drew. Idempotent: the backfill runs only while no server row exists.
-- EF is a pure mapper over this hand-authored schema (schema-first) — see persistence/database.md.
-- ============================================================

ALTER TABLE vitals_samples ALTER COLUMN target_id DROP NOT NULL;

INSERT INTO vitals_samples (id, target_id, server_id, sampled_at_utc, readable, load_percent, memory_percent,
                            disk_percent, containers, healthy_containers, restarts)
SELECT DISTINCT ON (server_id, sampled_at_utc)
       gen_random_uuid(), NULL, server_id, sampled_at_utc, readable, load_percent, memory_percent, disk_percent, 0, 0, 0
FROM vitals_samples
WHERE target_id IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM vitals_samples AS server_rows WHERE server_rows.target_id IS NULL)
ORDER BY server_id, sampled_at_utc,
         (readable AND (load_percent IS NOT NULL OR memory_percent IS NOT NULL OR disk_percent IS NOT NULL)) DESC,
         target_id;
