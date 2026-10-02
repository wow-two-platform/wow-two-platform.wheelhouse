-- Reverse 007-inventory — dev/test recovery only. Targets, vaults and servers go, and the placeholder servers table
-- comes back empty; products keep their rows but lose what the legacy registry lacked, and lifecycles move back into
-- product_metadata. Rows removed as legacy aliases do not come back.

DROP TABLE IF EXISTS vaults;
DROP TABLE IF EXISTS targets;
DROP TABLE IF EXISTS servers;

CREATE TABLE servers (
    id                  uuid        NOT NULL,
    name                text        NOT NULL,
    host                text        NOT NULL,
    ssh_port            integer     NOT NULL,
    ssh_user            text        NOT NULL,
    ssh_key_secret_id   uuid        NULL,
    hetzner_server_id   text        NULL,
    region              text        NULL,
    status              text        NOT NULL,
    created_at_utc      timestamptz NOT NULL,
    last_checked_at_utc timestamptz NULL,
    updated_at_utc      timestamptz NOT NULL,
    CONSTRAINT pk_servers PRIMARY KEY (id)
);
CREATE UNIQUE INDEX ix_servers_host ON servers (host);

CREATE TABLE product_metadata (
    slug           text        PRIMARY KEY,
    lifecycle      text        NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL
);
INSERT INTO product_metadata (slug, lifecycle, created_at_utc, updated_at_utc)
SELECT slug, lifecycle, created_at_utc, updated_at_utc FROM products;

ALTER TABLE products ADD COLUMN status text NULL;
UPDATE products
SET status = CASE lifecycle WHEN 'live' THEN 'active' WHEN 'paused' THEN 'paused' WHEN 'killed' THEN 'killed'
             ELSE 'draft' END;
ALTER TABLE products ALTER COLUMN status SET NOT NULL;
ALTER TABLE products DROP COLUMN release;
ALTER TABLE products DROP COLUMN lifecycle;
ALTER TABLE products DROP COLUMN default_branch;
ALTER TABLE products DROP COLUMN description;
ALTER TABLE products RENAME COLUMN repository TO repo;
