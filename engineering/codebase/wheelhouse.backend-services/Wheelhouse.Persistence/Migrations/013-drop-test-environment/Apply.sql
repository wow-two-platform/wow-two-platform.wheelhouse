-- ============================================================
-- 013-drop-test-environment — products work on `main` only: every release deploys to dev, then is promoted to prod.
-- The `test` environment is retired (owner decision 2026-10-17), so its targets go, with every row that depends on
-- one. Nothing references targets by foreign key; vitals readings name a target by its slug (vitals_samples.target_id)
-- and go with it, and a target's settings paths, smoke checks and sites are columns of its own row. Service statuses,
-- pins, vaults and secrets name no environment and stay. The deployment history (the runner's jobs/ records) and the
-- audit trail are not rows of a target: they keep every `test` entry, which reads as "test (retired)".
-- Forward-only: the rollback cannot recreate the deleted rows. Idempotent: a second run finds no test target.
-- EF is a pure mapper over this hand-authored schema (schema-first) — see persistence/database.md.
-- ============================================================

DELETE FROM vitals_samples
WHERE target_id IN (SELECT slug FROM targets WHERE environment = 'test');

DELETE FROM targets WHERE environment = 'test';
