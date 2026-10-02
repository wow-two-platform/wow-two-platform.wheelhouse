-- ============================================================
-- 007-inventory — the database becomes the source of truth for products, servers, targets and vaults.
-- Products: the legacy registry table grows into the full definition (description, default branch, lifecycle,
-- release source) and product_metadata folds back into it. ForeverPin and Wheelhouse, which the runner's catalog.py
-- defined, become rows; a legacy registry row naming one of their repositories under another slug was that product,
-- so it lends its lifecycle and goes. Servers: the placeholder table, which the API never wrote, is replaced.
-- Targets and vaults are new. The control plane exports these tables to <runner root>/inventory.json for the runner.
-- EF is a pure mapper over this hand-authored schema (schema-first) — see persistence/database.md.
-- ============================================================

ALTER TABLE products RENAME COLUMN repo TO repository;
ALTER TABLE products ADD COLUMN description    text  NOT NULL DEFAULT '';
ALTER TABLE products ADD COLUMN default_branch text  NOT NULL DEFAULT 'main';
ALTER TABLE products ADD COLUMN lifecycle      text  NULL;
ALTER TABLE products ADD COLUMN release        jsonb NULL;

UPDATE products p SET lifecycle = m.lifecycle FROM product_metadata m WHERE m.slug = p.slug;
UPDATE products
SET lifecycle = CASE status WHEN 'active' THEN 'live' WHEN 'paused' THEN 'paused' WHEN 'killed' THEN 'killed'
                ELSE 'building' END
WHERE lifecycle IS NULL;
ALTER TABLE products DROP COLUMN status;

INSERT INTO products (id, slug, name, description, repository, default_branch, lifecycle, release, created_at_utc,
                      updated_at_utc)
SELECT gen_random_uuid(), defined.slug, defined.name, defined.description, defined.repository, 'main',
       COALESCE((SELECT m.lifecycle FROM product_metadata m WHERE m.slug = defined.slug),
                (SELECT p.lifecycle FROM products p WHERE lower(p.repository) = lower(defined.repository) LIMIT 1),
                'building'),
       defined.release, now(), now()
FROM (VALUES
    ('foreverpin', 'ForeverPin', 'Styled QR codes and short links whose destination can change after printing.',
     'sulton-max/10x-venture-forever-pin',
     '{"asset":"foreverpin-release.tar.gz","workflow":"publish-docker-image.yml","images":[{"service":"management","image":"ghcr.io/sulton-max/10x-venture-forever-pin/management"},{"service":"redirect","image":"ghcr.io/sulton-max/10x-venture-forever-pin/redirect"}]}'::jsonb),
    ('wheelhouse', 'Wheelhouse', 'The portfolio''s deploy and operations control plane.',
     'wow-two-platform/wow-two-platform.wheelhouse', NULL::jsonb)
) AS defined (slug, name, description, repository, release)
ON CONFLICT (slug) DO NOTHING;

DELETE FROM products
WHERE slug NOT IN ('foreverpin', 'wheelhouse')
  AND lower(repository) IN ('sulton-max/10x-venture-forever-pin', 'wow-two-platform/wow-two-platform.wheelhouse');

ALTER TABLE products ALTER COLUMN lifecycle SET NOT NULL;
DROP TABLE product_metadata;

DROP TABLE servers;
CREATE TABLE servers (
    id             uuid        PRIMARY KEY,
    slug           text        NOT NULL,
    name           text        NOT NULL,
    provider       text        NOT NULL,
    host           text        NOT NULL,
    region         text        NOT NULL,
    ssh_user       text        NOT NULL,
    ssh_port       integer     NOT NULL,
    ingress        jsonb       NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL
);
CREATE UNIQUE INDEX ix_servers_slug ON servers (slug);

CREATE TABLE targets (
    id             uuid        PRIMARY KEY,
    slug           text        NOT NULL,
    product_id     uuid        NOT NULL REFERENCES products (id) ON DELETE RESTRICT,
    server_id      uuid        NOT NULL REFERENCES servers (id) ON DELETE RESTRICT,
    environment    text        NOT NULL,
    network        text        NOT NULL,
    root           text        NOT NULL,
    settings       jsonb       NOT NULL,
    smoke_checks   jsonb       NOT NULL,
    sites          jsonb       NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL
);
CREATE UNIQUE INDEX ix_targets_slug ON targets (slug);
CREATE INDEX ix_targets_product_id ON targets (product_id);
CREATE INDEX ix_targets_server_id ON targets (server_id);

CREATE TABLE vaults (
    id             uuid        PRIMARY KEY,
    slug           text        NOT NULL,
    name           text        NOT NULL,
    server_id      uuid        NOT NULL REFERENCES servers (id) ON DELETE RESTRICT,
    url            text        NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL
);
CREATE UNIQUE INDEX ix_vaults_slug ON vaults (slug);
CREATE INDEX ix_vaults_server_id ON vaults (server_id);
