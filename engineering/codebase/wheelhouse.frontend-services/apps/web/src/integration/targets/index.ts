import { z } from "zod";
import { DeploymentEnvironment, type InventoryTarget, type SaveTargetRequest, type SelfTarget } from "@/domain/targets";
import { requestData, requestEmpty } from "@/integration/common";

const TargetSchema = z.object({
  slug: z.string(),
  product: z.string(),
  server: z.string(),
  environment: z.enum(DeploymentEnvironment),
  network: z.string(),
  root: z.string(),
  settings: z.array(z.object({ service: z.string(), path: z.string() })),
  smokeChecks: z.array(z.object({ service: z.string(), path: z.string(), status: z.number().int() })),
  sites: z.array(z.object({ site: z.string(), host: z.string() })),
});

const SelfTargetSchema = z.object({
  product: z.string(),
  host: z.string().nullish().transform((value) => value ?? null),
  environments: z.array(
    z.object({
      environment: z.enum(DeploymentEnvironment),
      network: z.string(),
      root: z.string(),
      settings: z.array(z.object({ service: z.string(), path: z.string() })),
      sites: z.array(z.object({ site: z.string(), host: z.string() })),
    }),
  ),
});

/** Each product's environments on their servers, with explicit writes. */
export const targetsApi = {
  /** What Wheelhouse derives for its own environments from this console's own hostname. */
  getSelf: (signal?: AbortSignal) => requestData<SelfTarget>("/api/targets/self", SelfTargetSchema, { signal }),
  listTargets: (product: string | null, signal?: AbortSignal) =>
    requestData<InventoryTarget[]>(
      product ? `/api/targets?product=${encodeURIComponent(product)}` : "/api/targets",
      TargetSchema.array(),
      { signal },
    ),
  createTarget: (body: SaveTargetRequest, signal?: AbortSignal) =>
    requestData<InventoryTarget>("/api/targets", TargetSchema, { method: "POST", body, signal, action: "target" }),
  updateTarget: (slug: string, body: SaveTargetRequest, signal?: AbortSignal) =>
    requestData<InventoryTarget>(`/api/targets/${encodeURIComponent(slug)}`, TargetSchema, {
      method: "PUT", body, signal, action: "target",
    }),
  deleteTarget: (slug: string, signal?: AbortSignal) =>
    requestEmpty(`/api/targets/${encodeURIComponent(slug)}`, { method: "DELETE", signal, action: "target" }),
};
