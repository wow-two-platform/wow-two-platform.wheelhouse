-- ============================================================
-- 008-product-manifests — the cached copy of each product's manifest, product/product.json in its repository.
-- One row per product: the identity text read from the repository (name, description, homepage, per-service text)
-- and the commit it was read from, with the repository and branch that commit belongs to. A projection, never an
-- edit target: a row is replaced only by a copy read at a newer commit, dropped when a newer commit carries no
-- manifest, and removed with its product. Nothing expires it on a timer. Idempotent.
-- EF is a pure mapper over this hand-authored schema (schema-first) — see persistence/database.md.
-- ============================================================

CREATE TABLE IF NOT EXISTS product_manifests (
    product_id     uuid        PRIMARY KEY REFERENCES products (id) ON DELETE CASCADE,
    repository     text        NOT NULL,
    branch         text        NOT NULL,
    commit_sha     text        NOT NULL,
    name           text        NOT NULL,
    description    text        NOT NULL,
    homepage       text        NULL,
    services       jsonb       NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL
);
