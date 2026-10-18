import { z } from "zod";
import type { SystemStatus } from "@/domain/auth";
import { request } from "@/integration/common";

const SystemStatusSchema = z.object({
  service: z.string(),
  status: z.string(),
  version: z.string().optional(),
  environment: z.string().optional(),
  localRig: z.boolean().optional(),
});

/** The un-enveloped service liveness endpoint. */
export const systemApi = {
  getStatus: (signal?: AbortSignal) =>
    request<SystemStatus>("/api/system/status", SystemStatusSchema, { signal }),
};
