import type { SaveServerRequest, ServerVitals, Server, VitalsSample } from "@/domain/servers";
import { requestData, requestEmpty } from "@/integration/common";
import { ServerVitalsSchema, ServerSchema, VitalsSampleSchema } from "./schemas";

/** The server inventory, its edits, and host and container snapshots. */
export const serversApi = {
  listServers: (signal?: AbortSignal) =>
    requestData<Server[]>("/api/servers", ServerSchema.array(), { signal }),
  createServer: (body: SaveServerRequest, signal?: AbortSignal) =>
    requestData<Server>("/api/servers", ServerSchema, { method: "POST", body, signal, action: "server" }),
  updateServer: (slug: string, body: SaveServerRequest, signal?: AbortSignal) =>
    requestData<Server>(`/api/servers/${encodeURIComponent(slug)}`, ServerSchema, {
      method: "PUT", body, signal, action: "server",
    }),
  deleteServer: (slug: string, signal?: AbortSignal) =>
    requestEmpty(`/api/servers/${encodeURIComponent(slug)}`, { method: "DELETE", signal, action: "server" }),
  getVitals: (signal?: AbortSignal) =>
    requestData<ServerVitals>("/api/deployments/vitals", ServerVitalsSchema, {
      signal,
    }),
  /** Every target's stored readings of the last `hours` (1-720), oldest first. */
  getVitalsHistory: (hours: number, signal?: AbortSignal) =>
    requestData<VitalsSample[]>(
      `/api/deployments/vitals/history?hours=${hours}`,
      VitalsSampleSchema.array(),
      { signal },
    ),
};
