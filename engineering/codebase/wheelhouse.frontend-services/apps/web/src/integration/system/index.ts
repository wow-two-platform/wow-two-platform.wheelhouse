import { z } from "zod";
import type { SystemStatus } from "@/domain/auth";
import type { ConsolePeerReading } from "@/domain/system";
import { DeploymentEnvironment } from "@/domain/targets";
import { request, requestData } from "@/integration/common";

const SystemStatusSchema = z.object({
  service: z.string(),
  status: z.string(),
  version: z.string().optional(),
  environment: z.string().optional(),
  localRig: z.boolean().optional(),
});

const ConsolePeerSchema = z.object({
  url: z.string().nullish().transform((value) => value ?? null),
  environment: z.enum(DeploymentEnvironment).nullish().transform((value) => value ?? null),
});

/** The service liveness endpoint (un-enveloped) and the owner's read of the other console. */
export const systemApi = {
  getStatus: (signal?: AbortSignal) =>
    request<SystemStatus>("/api/system/status", SystemStatusSchema, { signal }),
  /** Where the other console answers and which environment it serves; empty until the host names a peer. */
  getPeer: (signal?: AbortSignal) =>
    requestData<ConsolePeerReading>("/api/system/peer", ConsolePeerSchema, { signal }),
};
