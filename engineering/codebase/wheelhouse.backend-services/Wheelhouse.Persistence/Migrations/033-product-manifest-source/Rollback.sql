-- Reverse 033-product-manifest-source — drop the column naming each cached manifest's file. Dev/test recovery only.
ALTER TABLE product_manifests DROP COLUMN IF EXISTS source_file;
