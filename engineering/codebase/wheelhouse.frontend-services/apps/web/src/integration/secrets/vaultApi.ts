import { z } from "zod";
import type { BuiltInVaultIssues, BuiltInVaultStatus, VaultImport, VaultVerification } from "@/domain/settings";
import { requestData } from "@/integration/common";

const nullableText = z.string().nullable().optional().transform((value) => value ?? null);

const StatusSchema = z.object({
  mode: z.enum(["builtin", "external"]).catch("builtin"),
  state: z.enum(["unsealed", "sealed"]).catch("sealed"),
  reason: nullableText,
  keyFile: z.string(),
  products: z.number().int(),
  secrets: z.number().int(),
  expiring: z.number().int(),
  expired: z.number().int(),
});

const IssuesSchema = z.object({
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

/** The built-in Secrets Vault: its state and issues, the full check and the import from a standalone vault. Counts and
 *  metadata only. */
export const vaultApi = {
  status: (signal?: AbortSignal) => requestData<BuiltInVaultStatus>("/api/vault", StatusSchema, { signal }),
  issues: (signal?: AbortSignal) => requestData<BuiltInVaultIssues>("/api/vault/issues", IssuesSchema, { signal }),
  verify: (signal?: AbortSignal) =>
    requestData<VaultVerification>("/api/vault/verify", VerificationSchema, { signal, method: "POST", action: "vault" }),
  importFrom: (vault: string, signal?: AbortSignal) =>
    requestData<VaultImport>("/api/vault/import", ImportSchema, { signal, method: "POST", action: "vault", body: { vault } }),
};
