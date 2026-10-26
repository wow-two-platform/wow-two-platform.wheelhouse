-- Reverse 032-root-key-checks — dev/test recovery only. The record of offline copy checks is lost with it; the next
-- check of the copy starts it again.

DROP TABLE IF EXISTS vault.vault_root_key_verifications;
