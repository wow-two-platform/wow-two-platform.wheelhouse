-- Reverse 034-server-console-url — drop the column holding each server's console address. Dev/test recovery only:
-- every card falls back to its provider's default sign-in page. Idempotent.
ALTER TABLE servers DROP COLUMN IF EXISTS console_url;
