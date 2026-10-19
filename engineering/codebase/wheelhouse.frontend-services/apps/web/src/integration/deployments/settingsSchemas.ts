import { z } from 'zod';

const DeploySettingSchema = z.object({
  key: z.string(),
  required: z.boolean(),
  secret: z.boolean(),
  source: z.enum(['derived', 'vault', 'stored', 'default', 'host', 'missing', 'unset']),
  value: z.string().nullable().optional().transform((value) => value ?? null),
  default: z.string().nullable().optional().transform((value) => value ?? null),
  derived: z.enum(['hosts', 'proxies', 'sites', 'database']).nullable().optional().transform((value) => value ?? null),
  allowed: z.array(z.string()).nullable().optional().transform((value) => value ?? null),
});

/** The settings plan of a release on a target; secret values never travel, so none is decoded. */
export const DeploySettingsSchema = z.object({
  target: z.string(),
  product: z.string(),
  environment: z.string(),
  release: z.string(),
  firstDeploy: z.boolean(),
  vault: z.object({ id: z.string(), namespace: z.string(), reachable: z.boolean() }).nullable(),
  services: z.array(z.object({ name: z.string(), hostFile: z.boolean(), keys: z.array(DeploySettingSchema) })),
  missing: z.number().int(),
});

/** What a save wrote. */
export const DeploySettingsSavedSchema = z.object({ settings: z.number().int(), secrets: z.number().int() });
