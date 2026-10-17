-- ============================================================
-- 010-workspaces — products are grouped into workspaces, such as the platform apps or one's own ventures.
-- A workspace has a fixed slug, a display name, an optional description and a place in the operator's order
-- (sort_order, lower first). Every product belongs to exactly one: products.workspace_id is required and holds its
-- workspace in place, so a workspace that still holds a product cannot be removed.
-- Backfill: the default workspace own-ventures ("Own ventures") is created first in the order and every existing
-- product moves into it. Idempotent.
-- EF is a pure mapper over this hand-authored schema (schema-first) — see persistence/database.md.
-- ============================================================

CREATE TABLE IF NOT EXISTS workspaces (
    id             uuid        PRIMARY KEY,
    slug           text        NOT NULL,
    name           text        NOT NULL,
    description    text        NULL,
    sort_order     integer     NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ix_workspaces_slug ON workspaces (slug);

INSERT INTO workspaces (id, slug, name, description, sort_order, created_at_utc, updated_at_utc)
VALUES (gen_random_uuid(), 'own-ventures', 'Own ventures', NULL, 0, now(), now())
ON CONFLICT (slug) DO NOTHING;

ALTER TABLE products ADD COLUMN IF NOT EXISTS workspace_id uuid NULL;
UPDATE products SET workspace_id = (SELECT id FROM workspaces WHERE slug = 'own-ventures') WHERE workspace_id IS NULL;
ALTER TABLE products ALTER COLUMN workspace_id SET NOT NULL;
ALTER TABLE products DROP CONSTRAINT IF EXISTS fk_products_workspaces_workspace_id;
ALTER TABLE products ADD CONSTRAINT fk_products_workspaces_workspace_id
    FOREIGN KEY (workspace_id) REFERENCES workspaces (id) ON DELETE RESTRICT;
CREATE INDEX IF NOT EXISTS ix_products_workspace_id ON products (workspace_id);
