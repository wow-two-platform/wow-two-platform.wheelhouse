-- ============================================================
-- 031-product-components — a product may span several repositories (decision 70).
-- products.components lists a product's further repositories after its primary one, each {repository, release}: its
-- own release source (asset, workflow, images), in the order the product's release sets name them. A product of one
-- repository keeps an empty list, so every existing row reads as before. Expand only (decision 65). Idempotent.
-- EF is a pure mapper over this hand-authored schema (schema-first) — see persistence/database.md.
-- ============================================================

ALTER TABLE products ADD COLUMN IF NOT EXISTS components jsonb NOT NULL DEFAULT '[]'::jsonb;
