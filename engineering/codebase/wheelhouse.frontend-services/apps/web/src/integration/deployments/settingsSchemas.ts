import { z } from 'zod';

const nullable = <T extends z.ZodType>(schema: T) => schema.nullable().optional().transform((value) => value ?? null);

/** A secret's metadata: version, who and when, fingerprint and expiry; the value never travels. */
export const SecretMetaSchema = z.object({
  key: z.string(),
  version: z.number().int(),
  updatedAt: z.string(),
  updatedBy: z.string(),
  fingerprint: z.string(),
  expiresAt: nullable(z.string()),
  expiry: z.enum(['expired', '7d', '30d']).nullable().optional().catch(null).transform((value) => value ?? null),
});

const DeploySettingSchema = z.object({
  key: z.string(),
  required: z.boolean(),
  secret: z.boolean(),
  source: z.enum(['derived', 'vault', 'stored', 'default', 'host', 'missing', 'unset']),
  value: nullable(z.string()),
  default: nullable(z.string()),
  derived: nullable(z.enum(['hosts', 'proxies', 'sites', 'database'])),
  allowed: nullable(z.array(z.string())),
  type: z.enum(['text', 'number', 'boolean', 'choice', 'url', 'cron', 'date', 'email']).catch('text').default('text'),
  format: nullable(z.string()),
  adHoc: z.boolean().default(false),
  looksSecret: z.boolean().default(false),
  vault: nullable(SecretMetaSchema),
});

/** The settings plan of a release on a target; secret values never travel, so none is decoded. */
export const DeploySettingsSchema = z.object({
  target: z.string(),
  product: z.string(),
  environment: z.string(),
  release: nullable(z.string()),
  bundle: nullable(z.string()),
  firstDeploy: z.boolean(),
  vault: nullable(
    z.object({
      id: z.string(),
      namespace: z.string(),
      reachable: z.boolean(),
      mode: z.enum(['builtin', 'external']).catch('builtin').default('builtin'),
      sealed: nullable(z.string()),
    }),
  ),
  services: z.array(z.object({ name: z.string(), hostFile: z.boolean(), keys: z.array(DeploySettingSchema) })),
  extra: z.array(DeploySettingSchema).default([]),
  unapplied: z.array(z.string()).default([]),
  pending: nullable(z.object({ at: z.string(), by: z.string() }).passthrough()),
  missing: z.number().int(),
});

/** What a save wrote, with each key's new version. */
export const DeploySettingsSavedSchema = z.object({
  settings: z.number().int(),
  secrets: z.number().int(),
  keys: z
    .array(
      z.object({
        key: z.string(),
        secret: z.boolean(),
        removed: z.boolean(),
        version: nullable(z.number().int()),
        fingerprint: nullable(z.string()),
        generated: z.boolean().optional(),
      }),
    )
    .default([]),
});

/** An apply submitted now or scheduled. */
export const SettingsApplySchema = z.object({
  target: z.string(),
  mode: z.enum(['now', 'scheduled']),
  at: nullable(z.string()),
  by: z.string(),
  deployment: nullable(z.string()),
  release: nullable(z.string()),
});

/** A secret's history, never a value. */
export const SecretHistorySchema = z.object({
  target: z.string(),
  key: z.string(),
  removed: z.boolean(),
  expiresAt: nullable(z.string()),
  versions: z.array(
    z.object({
      version: z.number().int(),
      createdBy: z.string(),
      createdAt: z.string(),
      fingerprint: z.string(),
      note: nullable(z.string()),
      origin: nullable(z.string()),
      current: z.boolean(),
    }),
  ),
});

/** A restored version. */
export const SecretWrittenSchema = z.object({ key: z.string(), version: z.number().int(), fingerprint: z.string() });

/** A key the owner added, as the plan lists it. */
export const AddedKeySchema = DeploySettingSchema;
