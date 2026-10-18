import { computed } from "vue";
import { useAppMutation, useAppQuery, useOptimisticMutation } from "@/bootstrap/query";
import { Ordering } from "@/domain/common";
import type { CreateWorkspaceRequest, UpdateWorkspaceRequest, Workspace } from "@/domain/workspaces";
import { workspacesApi } from "@/integration/workspaces";
import { ProductKeys } from "@/application/products";

/** Query keys for workspaces. */
export const WorkspaceKeys = { list: ["workspaces"] as const };

/** The workspaces in the owner's order, with their edits and an optimistic reorder. */
export function useWorkspaces() {
  const list = useAppQuery({
    key: WorkspaceKeys.list,
    queryFn: ({ signal }) => workspacesApi.listWorkspaces(signal),
  });
  const create = useAppMutation({
    mutationFn: (body: CreateWorkspaceRequest, { signal }) => workspacesApi.createWorkspace(body, signal),
    invalidates: () => [WorkspaceKeys.list],
    meta: { suppressGlobalError: true },
  });
  const update = useAppMutation({
    mutationFn: ({ id, body }: { id: string; body: UpdateWorkspaceRequest }, { signal }) =>
      workspacesApi.updateWorkspace(id, body, signal),
    invalidates: () => [WorkspaceKeys.list],
    meta: { suppressGlobalError: true },
  });
  const remove = useAppMutation({
    mutationFn: (id: string, { signal }) => workspacesApi.deleteWorkspace(id, signal),
    invalidates: () => [WorkspaceKeys.list, ProductKeys.list],
    meta: { suppressGlobalError: true },
  });
  const reorder = useOptimisticMutation({
    mutationFn: (ids: readonly string[], { signal }) => workspacesApi.reorderWorkspaces(ids, signal),
    targets: [
      {
        key: WorkspaceKeys.list,
        apply: (current: Workspace[] | undefined, ids: readonly string[]) =>
          (current ? Ordering.apply(current, ids) : current) as Workspace[],
      },
    ],
  });
  const workspaces = computed(() => Ordering.sort(list.data.value ?? []));

  return {
    /** The workspaces in custom order. */
    workspaces,
    isFirstLoad: list.isFirstLoad,
    hasData: computed(() => list.data.value !== undefined),
    error: computed(() => list.error.value?.message ?? null),
    reload: async () => {
      await list.refetch();
    },
    create: (body: CreateWorkspaceRequest) => create.mutateAsync(body),
    update: (id: string, body: UpdateWorkspaceRequest) => update.mutateAsync({ id, body }),
    remove: (id: string) => remove.mutateAsync(id),
    /** Moves one workspace; the list changes at once and rolls back if the host refuses. */
    move: (from: number, to: number) => {
      const ids = Ordering.move(workspaces.value, from, to).map((workspace) => workspace.id);
      return reorder.mutateAsync(ids);
    },
  };
}

/** Operations the workspace forms receive. */
export type WorkspaceOperations = ReturnType<typeof useWorkspaces>;
