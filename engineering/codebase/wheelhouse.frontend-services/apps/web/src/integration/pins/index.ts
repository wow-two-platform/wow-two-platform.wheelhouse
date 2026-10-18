import { z } from "zod";
import { PinKind, type CreatePinRequest, type Pin } from "@/domain/pins";
import { requestAccepted, requestData } from "@/integration/common";

const PinSchema = z.object({
  id: z.string(),
  kind: z.enum(PinKind),
  workspaceId: z.string().nullish().transform((value) => value ?? null),
  productId: z.string().nullish().transform((value) => value ?? null),
  service: z.string().nullish().transform((value) => value ?? null),
  order: z.number().int(),
});

/** The owner's pins; writes carry the `pin` action. */
export const pinsApi = {
  listPins: (signal?: AbortSignal) => requestData<Pin[]>("/api/pins", PinSchema.array(), { signal }),
  /** Pins a workspace, product or service; pinning one twice answers the existing pin. */
  createPin: (body: CreatePinRequest, signal?: AbortSignal) =>
    requestAccepted("/api/pins", { method: "POST", body, signal, action: "pin" }),
  deletePin: (id: string, signal?: AbortSignal) =>
    requestAccepted(`/api/pins/${encodeURIComponent(id)}`, { method: "DELETE", signal, action: "pin" }),
  /** Saves the custom order; the list names every pin once. */
  reorderPins: (ids: readonly string[], signal?: AbortSignal) =>
    requestAccepted("/api/pins/order", { method: "PUT", body: { ids }, signal, action: "pin" }),
};
