-- ============================================================
-- 030-built-in-vault — Secrets Vault as Wheelhouse's built-in module (decisions 59-63), expand only (decision 65).
--
-- The vault schema holds secret values, encrypted: each product's data key wrapped by the root key file (never in the
-- database), each secret's versions encrypted under its product's key with a keyed fingerprint, and the canary that
-- proves a root key belongs to this vault before and after a restore. Rows carry metadata in the clear: key names,
-- versions, who and when, expiry. A product's keys and secrets go with the product.
--
-- Beside it, in public: the keys an owner adds to a product in the console with a type (product_setting_keys), what
-- each deploy rendered, by fingerprint and without values (deployment_renders), and a scheduled apply of edited
-- settings per target (settings_applies). Idempotent. EF is a pure mapper over this hand-authored schema.
-- ============================================================

CREATE SCHEMA IF NOT EXISTS vault;

CREATE TABLE IF NOT EXISTS vault.vault_canary (
    id             integer     PRIMARY KEY CHECK (id = 1),
    wrapped_key    bytea       NOT NULL,
    key_nonce      bytea       NOT NULL,
    key_tag        bytea       NOT NULL,
    cipher_text    bytea       NOT NULL,
    nonce          bytea       NOT NULL,
    tag            bytea       NOT NULL,
    digest         bytea       NOT NULL,
    created_at_utc timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS vault.vault_product_keys (
    product_id     uuid        PRIMARY KEY REFERENCES products (id) ON DELETE CASCADE,
    wrapped_key    bytea       NOT NULL,
    nonce          bytea       NOT NULL,
    tag            bytea       NOT NULL,
    created_at_utc timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS vault.vault_secrets (
    id              uuid        PRIMARY KEY,
    product_id      uuid        NOT NULL REFERENCES products (id) ON DELETE CASCADE,
    environment     text        NOT NULL,
    key             text        NOT NULL,
    current_version integer     NOT NULL,
    expires_at_utc  timestamptz NULL,
    removed_at_utc  timestamptz NULL,
    created_at_utc  timestamptz NOT NULL,
    updated_at_utc  timestamptz NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_vault_secrets_key ON vault.vault_secrets (product_id, environment, key);

CREATE TABLE IF NOT EXISTS vault.vault_secret_versions (
    id             uuid        PRIMARY KEY,
    secret_id      uuid        NOT NULL REFERENCES vault.vault_secrets (id) ON DELETE CASCADE,
    version        integer     NOT NULL,
    cipher_text    bytea       NOT NULL,
    nonce          bytea       NOT NULL,
    tag            bytea       NOT NULL,
    fingerprint    text        NOT NULL,
    note           text        NULL,
    origin         text        NULL,
    created_by     text        NOT NULL,
    created_at_utc timestamptz NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_vault_secret_versions_version ON vault.vault_secret_versions (secret_id, version);

CREATE TABLE IF NOT EXISTS product_setting_keys (
    product_id     uuid        NOT NULL REFERENCES products (id) ON DELETE CASCADE,
    key            text        NOT NULL,
    secret         boolean     NOT NULL,
    type           text        NOT NULL,
    format         text        NULL,
    choices        text        NULL,
    required       boolean     NOT NULL,
    note           text        NULL,
    created_by     text        NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    PRIMARY KEY (product_id, key)
);

CREATE TABLE IF NOT EXISTS deployment_renders (
    job_id    uuid        PRIMARY KEY,
    target    text        NOT NULL,
    bundle_id text        NULL,
    keys      text        NOT NULL,
    changes   text        NULL,
    at_utc    timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_deployment_renders_target ON deployment_renders (target, at_utc);

CREATE TABLE IF NOT EXISTS settings_applies (
    target_id      uuid        PRIMARY KEY REFERENCES targets (id) ON DELETE CASCADE,
    at_utc         timestamptz NOT NULL,
    requested_by   text        NOT NULL,
    reason         text        NULL,
    created_at_utc timestamptz NOT NULL
);
