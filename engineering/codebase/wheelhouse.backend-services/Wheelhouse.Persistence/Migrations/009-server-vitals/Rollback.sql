-- Reverse 009-server-vitals — dev/test recovery only. Server host rows go and target_id is required again. Target
-- rows sampled since the migration hold no host figures, so host trends have a gap until the next sampling pass.

DELETE FROM vitals_samples WHERE target_id IS NULL;
ALTER TABLE vitals_samples ALTER COLUMN target_id SET NOT NULL;
