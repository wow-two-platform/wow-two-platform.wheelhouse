-- ============================================================
-- 033-product-manifest-source — the repository file each cached manifest was read from. A product's identity moved
-- into its deploy descriptor (engineering/deployment/deploy.yml) on 2026-10-27; the console still reads a retired
-- product/product.json while the descriptor declares none, and tells the owner to move it. Every row cached before
-- this migration was read from product/product.json, which is why that is the default, and why a previous release
-- that inserts without the column still records the truth. Expands only (decision 65). Idempotent.
-- ============================================================

ALTER TABLE product_manifests
    ADD COLUMN IF NOT EXISTS source_file text NOT NULL DEFAULT 'product/product.json';
