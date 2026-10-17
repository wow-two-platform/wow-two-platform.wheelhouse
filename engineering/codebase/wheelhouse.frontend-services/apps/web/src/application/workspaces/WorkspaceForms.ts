import { z } from "zod";
import { InventoryRules } from "@/domain/common";
import type { CreateWorkspaceRequest, UpdateWorkspaceRequest, Workspace } from "@/domain/workspaces";

/** A workspace's editable fields; the slug only while it is new. */
export interface WorkspaceFormModel {
  slug: string;
  name: string;
  description: string;
}

/** Validates what the form can judge alone; the host checks the rest and names the field it refuses. */
export const workspaceFormSchema = z.object({
  slug: z.string().trim().regex(InventoryRules.slug, "Start with a lowercase letter; use lowercase letters, digits and dashes."),
  name: z.string().trim().min(1, "Enter a name."),
  description: z.string().trim(),
});

/** Detached editing state from a workspace, or blank for a new one. */
export function workspaceFormOf(workspace: Workspace | null): WorkspaceFormModel {
  return { slug: workspace?.slug ?? "", name: workspace?.name ?? "", description: workspace?.description ?? "" };
}

/** The request a new workspace sends. */
export function createWorkspaceRequestOf(values: WorkspaceFormModel): CreateWorkspaceRequest {
  return { slug: values.slug, name: values.name, ...(values.description ? { description: values.description } : {}) };
}

/** The request a renamed workspace sends; an emptied description clears it. */
export function updateWorkspaceRequestOf(values: WorkspaceFormModel): UpdateWorkspaceRequest {
  return { name: values.name, description: values.description };
}
