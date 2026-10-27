-- ============================================================
-- 034-server-console-url — the provider console a server's card opens, when it is not the provider's default sign-in
-- page: an account's own manager, such as OVHcloud's Canadian one. The console knows each provider's default; a row
-- with none keeps it. An https address only, checked by the API; never a credential. Expands only (decision 65).
-- Idempotent.
-- ============================================================

ALTER TABLE servers ADD COLUMN IF NOT EXISTS console_url text NULL;
