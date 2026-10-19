-- ============================================================
-- 025-product-deploy-mode — what deploys each product, stored on its row.
-- products.deploy is the catalog entry's deploy: wheelhouse (Wheelhouse deploys it, automatic deploys and promotions
-- apply), ci (its own CI deploys it) or none (nothing does). A catalog import sets it from the entry and the product
-- form edits it. Existing rows: the wheelhouse product, which its own CI deploys, becomes ci; every other product
-- wheelhouse. This replaces the console's and the API's checks for the wheelhouse slug. Idempotent: the backfill runs
-- only on rows the column is new to.
-- EF is a pure mapper over this hand-authored schema (schema-first) — see persistence/database.md.
-- ============================================================

ALTER TABLE products ADD COLUMN IF NOT EXISTS deploy text NULL;

UPDATE products SET deploy = CASE WHEN slug = 'wheelhouse' THEN 'ci' ELSE 'wheelhouse' END WHERE deploy IS NULL;

ALTER TABLE products ALTER COLUMN deploy SET DEFAULT 'wheelhouse';
ALTER TABLE products ALTER COLUMN deploy SET NOT NULL;
