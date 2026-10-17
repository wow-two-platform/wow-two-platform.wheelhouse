-- ============================================================
-- 012-pins — the workspaces, products and services an operator keeps one click away, in the operator's own order.
-- A pin belongs to one operator (owner = the signed-in GitHub login) and names exactly the target its kind takes:
-- a workspace (workspace_id), a product (product_id) or a service (product_id + service, the service's key in the
-- product's deploy.yml, with no environment: one pin covers them all). An owner pins a target once; the target's null
-- parts compare as equal (NULLS NOT DISTINCT, PostgreSQL 15+). A pin goes with its workspace or product. sort_order
-- orders one owner's pins, lower first. A preference, not inventory: pins never reach the audit trail. Idempotent.
-- EF is a pure mapper over this hand-authored schema (schema-first) — see persistence/database.md.
-- ============================================================

CREATE TABLE IF NOT EXISTS pins (
    id             uuid        PRIMARY KEY,
    owner          text        NOT NULL,
    kind           text        NOT NULL,
    workspace_id   uuid        NULL REFERENCES workspaces (id) ON DELETE CASCADE,
    product_id     uuid        NULL REFERENCES products (id) ON DELETE CASCADE,
    service        text        NULL,
    sort_order     integer     NOT NULL,
    created_at_utc timestamptz NOT NULL,
    CONSTRAINT ck_pins_target CHECK (
        (kind = 'workspace' AND workspace_id IS NOT NULL AND product_id IS NULL AND service IS NULL) OR
        (kind = 'product' AND workspace_id IS NULL AND product_id IS NOT NULL AND service IS NULL) OR
        (kind = 'service' AND workspace_id IS NULL AND product_id IS NOT NULL AND service IS NOT NULL))
);
CREATE UNIQUE INDEX IF NOT EXISTS ix_pins_target ON pins (owner, kind, workspace_id, product_id, service) NULLS NOT DISTINCT;
CREATE INDEX IF NOT EXISTS ix_pins_owner_order ON pins (owner, sort_order);
