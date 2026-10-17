-- Reverse 011-product-status — dev/test recovery only. Service statuses go, and the lifecycle comes back from the
-- status: inactive becomes paused and active becomes building, so live and killed are not restored.

DROP TABLE IF EXISTS service_statuses;
ALTER TABLE products ADD COLUMN IF NOT EXISTS lifecycle text NULL;
UPDATE products SET lifecycle = CASE status WHEN 'inactive' THEN 'paused' ELSE 'building' END WHERE lifecycle IS NULL;
ALTER TABLE products ALTER COLUMN lifecycle SET NOT NULL;
ALTER TABLE products DROP COLUMN IF EXISTS status;
