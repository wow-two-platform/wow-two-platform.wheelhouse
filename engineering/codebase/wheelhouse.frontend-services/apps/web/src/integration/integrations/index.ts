import { z } from "zod";
import type {
  CreateIntegrationKeyRequest,
  IntegrationKey,
  IntegrationKeyWithSecret,
} from "@/domain/integrations";
import { requestData } from "@/integration/common";

const IntegrationKeySchema = z.object({
  id: z.string(),
  name: z.string(),
  prefix: z.string(),
  scopes: z.array(z.string()),
  createdBy: z.string(),
  createdAt: z.string(),
  lastUsedAt: z.string().nullable(),
  revokedAt: z.string().nullable(),
});

/** Integration keys: created and revoked by the owner, each secret returned once. */
export const integrationKeysApi = {
  listKeys: (signal?: AbortSignal) =>
    requestData<IntegrationKey[]>("/api/integration-keys", IntegrationKeySchema.array(), { signal }),
  createKey: (body: CreateIntegrationKeyRequest, signal?: AbortSignal) =>
    requestData<IntegrationKeyWithSecret>(
      "/api/integration-keys",
      z.object({ key: IntegrationKeySchema, secret: z.string() }),
      { method: "POST", body, signal, action: "key-create" },
    ),
  revokeKey: (id: string, signal?: AbortSignal) =>
    requestData<IntegrationKey>(
      `/api/integration-keys/${encodeURIComponent(id)}/revoke`,
      IntegrationKeySchema,
      { method: "POST", signal, action: "key-revoke" },
    ),
};
