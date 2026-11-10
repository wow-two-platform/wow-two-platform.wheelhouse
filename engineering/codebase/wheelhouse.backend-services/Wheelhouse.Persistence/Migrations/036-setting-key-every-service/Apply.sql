-- ============================================================
-- 036-setting-key-every-service — whether a key the owner added goes to every service even while no release declares
-- it. Off by default: such a key waits for a release that declares it, so a key typed ahead of its release never reaches
-- a service that does not take it (events-integration 2026-11-10: two Identity config checks refused keys typed before
-- their names). Every row the column finds was added ahead of its release, so it starts off. Expands only
-- (decision 65). Idempotent.
-- ============================================================

ALTER TABLE product_setting_keys ADD COLUMN IF NOT EXISTS every_service boolean NOT NULL DEFAULT false;
