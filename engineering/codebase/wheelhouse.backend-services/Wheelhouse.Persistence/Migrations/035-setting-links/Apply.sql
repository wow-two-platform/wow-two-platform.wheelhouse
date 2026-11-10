-- ============================================================
-- 035-setting-links — an owner's link from a product environment's setting key to another product's key in the same
-- environment (a client secret one product's vault generates and another product presents). Each deploy renders the
-- source's current value under the linking key; the console sees the source's name and metadata, never a value. One
-- link per product, environment and key; a link goes with its product and with its source's target. Expands only
-- (decision 65). Idempotent. EF is a pure mapper over this hand-authored schema.
-- ============================================================

CREATE TABLE IF NOT EXISTS setting_links (
    product_id       uuid        NOT NULL REFERENCES products (id) ON DELETE CASCADE,
    environment      text        NOT NULL,
    key              text        NOT NULL,
    source_target_id uuid        NOT NULL REFERENCES targets (id) ON DELETE CASCADE,
    source_key       text        NOT NULL,
    created_by       text        NOT NULL,
    created_at_utc   timestamptz NOT NULL,
    PRIMARY KEY (product_id, environment, key)
);
