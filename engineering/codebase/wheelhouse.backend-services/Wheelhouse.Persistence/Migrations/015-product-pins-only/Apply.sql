-- ============================================================
-- 015-product-pins-only — pins are products only (owner decision 2026-10-18): the sidebar's Products tree lists the
-- pinned products, and it has no workspace tree any more. A workspace pin goes. A service pin becomes a pin of its
-- product, so the product stays one click away, unless the owner already pinned that product; then the service pins
-- go. Each owner's remaining pins are renumbered 0..n-1 in their previous order, service pins taking the place of the
-- first one. The table keeps its shape; the API refuses workspace and service pins from now on.
-- Forward-only: the rollback cannot tell a converted pin from one the owner made. Idempotent: a second run finds no
-- workspace or service pin and renumbers an already dense order to itself.
-- EF is a pure mapper over this hand-authored schema (schema-first) — see persistence/database.md.
-- ============================================================

-- A service pin's product, pinned once per owner at the place of its first service pin, when not pinned already.
INSERT INTO pins (id, owner, kind, workspace_id, product_id, service, sort_order, created_at_utc)
SELECT gen_random_uuid(), service_pins.owner, 'product', NULL, service_pins.product_id, NULL,
       service_pins.sort_order, service_pins.created_at_utc
FROM (
    SELECT DISTINCT ON (owner, product_id) owner, product_id, sort_order, created_at_utc
    FROM pins
    WHERE kind = 'service'
    ORDER BY owner, product_id, sort_order, created_at_utc
) AS service_pins
WHERE NOT EXISTS (
    SELECT 1 FROM pins AS pinned
    WHERE pinned.owner = service_pins.owner AND pinned.kind = 'product' AND pinned.product_id = service_pins.product_id
);

DELETE FROM pins WHERE kind IN ('workspace', 'service');

-- Dense order per owner; a converted pin shares its service pin's old place, so creation breaks the tie.
UPDATE pins
SET sort_order = ranked.position
FROM (
    SELECT id, (row_number() OVER (PARTITION BY owner ORDER BY sort_order, created_at_utc, id) - 1)::integer AS position
    FROM pins
) AS ranked
WHERE pins.id = ranked.id AND pins.sort_order <> ranked.position;
