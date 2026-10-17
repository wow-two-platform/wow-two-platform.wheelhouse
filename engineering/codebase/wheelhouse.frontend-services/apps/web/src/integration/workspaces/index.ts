import { z } from "zod";
import type { CreateWorkspaceRequest, UpdateWorkspaceRequest, Workspace } from "@/domain/workspaces";
import { requestAccepted, requestData, requestEmpty } from "@/integration/common";

/** A workspace without a description reads as an empty one. */
const WorkspaceSchema = z.object({
  id: z.string(),
  slug: z.string(),
  name: z.string(),
  description: z
    .string()
    .nullish()
    .transform((value) => value ?? ""),
  order: z.number().int(),
});

/** The workspaces products are grouped into; writes carry the `workspace` action. */
export const workspacesApi = {
  listWorkspaces: (signal?: AbortSignal) =>
    requestData<Workspace[]>("/api/workspaces", WorkspaceSchema.array(), { signal }),
  createWorkspace: (body: CreateWorkspaceRequest, signal?: AbortSignal) =>
    requestData<Workspace>("/api/workspaces", WorkspaceSchema, { method: "POST", body, signal, action: "workspace" }),
  updateWorkspace: (id: string, body: UpdateWorkspaceRequest, signal?: AbortSignal) =>
    requestData<Workspace>(`/api/workspaces/${encodeURIComponent(id)}`, WorkspaceSchema, {
      method: "PUT", body, signal, action: "workspace",
    }),
  deleteWorkspace: (id: string, signal?: AbortSignal) =>
    requestEmpty(`/api/workspaces/${encodeURIComponent(id)}`, { method: "DELETE", signal, action: "workspace" }),
  /** Saves the custom order; the list names every workspace once. */
  reorderWorkspaces: (ids: readonly string[], signal?: AbortSignal) =>
    requestAccepted("/api/workspaces/order", { method: "PUT", body: { ids }, signal, action: "workspace" }),
};
