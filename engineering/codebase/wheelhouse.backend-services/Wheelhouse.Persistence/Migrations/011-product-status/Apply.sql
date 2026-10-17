-- ============================================================
-- 011-product-status — a binary status replaces the five-value product lifecycle.
-- A product is active or inactive (products.status); inactive only hides it from the console's lists, so it still
-- deploys and reads. Mapping: paused and killed become inactive, idea, building and live become active; the lifecycle
-- column goes. A service's status is a row in service_statuses keyed by (product_id, service), the service's key in
-- the product's deploy.yml; a service without a row is active, and its rows go with the product. Idempotent: the
-- mapping runs only while the lifecycle column exists.
-- EF is a pure mapper over this hand-authored schema (schema-first) — see persistence/database.md.
-- ============================================================

ALTER TABLE products ADD COLUMN IF NOT EXISTS status text NULL;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.columns
               WHERE table_schema = current_schema() AND table_name = 'products' AND column_name = 'lifecycle') THEN
        UPDATE products
        SET status = CASE WHEN lifecycle IN ('paused', 'killed') THEN 'inactive' ELSE 'active' END
        WHERE status IS NULL;
        ALTER TABLE products DROP COLUMN lifecycle;
    END IF;
END $$;

UPDATE products SET status = 'active' WHERE status IS NULL;
ALTER TABLE products ALTER COLUMN status SET NOT NULL;

CREATE TABLE IF NOT EXISTS service_statuses (
    product_id     uuid        NOT NULL REFERENCES products (id) ON DELETE CASCADE,
    service        text        NOT NULL,
    status         text        NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    PRIMARY KEY (product_id, service)
);
