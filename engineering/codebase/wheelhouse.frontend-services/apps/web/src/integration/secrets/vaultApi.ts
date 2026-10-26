import { z } from "zod";
import type {
  BuiltInVaultIssues,
  BuiltInVaultStatus,
  RootKeyOfflineCopy,
  VaultImport,
  VaultVerification,
} from "@/domain/settings";
import { requestData } from "@/integration/common";

const nullableText = z.string().nullable().optional().transform((value) => value ?? null);

const OfflineCopySchema = z.object({
  fingerprint: nullableText,
  verifiedAt: nullableText,
  verifiedBy: nullableText,
  reason: nullableText,
  changed: z.boolean().catch(false),
  dueAt: nullableText,
  due: z.boolean().catch(false),
  intervalDays: z.number().int(),
});

const FileCheckSchema = z.object({ at: z.string(), problem: nullableText });

/** Absent on a console from before the root key checks: reads as null. */
const optional = <T extends z.ZodType>(schema: T) => schema.nullable().optional().transform((value) => value ?? null);

export const VaultStatusSchema = z.object({
  mode: z.enum(["builtin", "external"]).catch("builtin"),
  state: z.enum(["unsealed", "sealed"]).catch("sealed"),
  reason: nullableText,
  keyFile: z.string(),
  products: z.number().int(),
  secrets: z.number().int(),
  expiring: z.number().int(),
  expired: z.number().int(),
  offlineCopy: optional(OfflineCopySchema),
  keyFileCheck: optional(FileCheckSchema),
});

export const VaultIssuesSchema = z.object({
  state: z.enum(["unsealed", "sealed"]).catch("sealed"),
  reason: nullableText,
  expiries: z.array(
    z.object({
      product: z.string(),
      environment: z.string(),
      target: nullableText,
      key: z.string(),
      expiresAt: z.string(),
      level: z.enum(["expired", "7d", "30d"]),
    }),
  ),
  offlineCopy: optional(OfflineCopySchema),
  keyFile: optional(FileCheckSchema),
});

const VerificationSchema = z.object({
  state: z.enum(["unsealed", "sealed"]).catch("sealed"),
  canary: z.boolean(),
  products: z.number().int(),
  secrets: z.number().int(),
  versions: z.number().int(),
  failed: z.number().int(),
  failures: z.array(z.string()).default([]),
});

const ImportSchema = z.object({
  source: z.string(),
  namespaces: z.number().int(),
  unmatched: z.array(z.string()).default([]),
  secrets: z.number().int(),
  versions: z.number().int(),
  skipped: z.number().int(),
  verified: z.number().int(),
  failed: z.number().int(),
  failures: z.array(z.string()).default([]),
});

/** The built-in Secrets Vault: its state and issues, the full check, the offline root key copy's check and the import
 *  from a standalone vault. Counts, metadata and the root key's fingerprint only; the key itself is never sent. */
export const vaultApi = {
  status: (signal?: AbortSignal) => requestData<BuiltInVaultStatus>("/api/vault", VaultStatusSchema, { signal }),
  issues: (signal?: AbortSignal) => requestData<BuiltInVaultIssues>("/api/vault/issues", VaultIssuesSchema, { signal }),
  verifyOfflineCopy: (fingerprint: string, reason: string, signal?: AbortSignal) =>
    requestData<RootKeyOfflineCopy>("/api/vault/offline-copy", OfflineCopySchema, {
      signal,
      method: "POST",
      action: "vault",
      body: { fingerprint, reason },
    }),
  verify: (signal?: AbortSignal) =>
    requestData<VaultVerification>("/api/vault/verify", VerificationSchema, { signal, method: "POST", action: "vault" }),
  importFrom: (vault: string, signal?: AbortSignal) =>
    requestData<VaultImport>("/api/vault/import", ImportSchema, { signal, method: "POST", action: "vault", body: { vault } }),
};
