-- Reverse 030-built-in-vault — dev/test recovery only. Every secret value the built-in vault holds is lost with it;
-- the root key file stays where it is.

DROP TABLE IF EXISTS settings_applies;
DROP TABLE IF EXISTS deployment_renders;
DROP TABLE IF EXISTS product_setting_keys;
DROP TABLE IF EXISTS vault.vault_secret_versions;
DROP TABLE IF EXISTS vault.vault_secrets;
DROP TABLE IF EXISTS vault.vault_product_keys;
DROP TABLE IF EXISTS vault.vault_canary;
DROP SCHEMA IF EXISTS vault;
