-- Reverse 025-product-deploy-mode — dev/test recovery only. The stored modes go; the code that reads them goes with it.

ALTER TABLE products DROP COLUMN IF EXISTS deploy;
