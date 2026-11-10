-- Reverse 036-setting-key-every-service — drop the column choosing whether an added key goes to every service. Dev/test
-- recovery only: the console before it gives every undeclared key to every service. Idempotent.
ALTER TABLE product_setting_keys DROP COLUMN IF EXISTS every_service;
