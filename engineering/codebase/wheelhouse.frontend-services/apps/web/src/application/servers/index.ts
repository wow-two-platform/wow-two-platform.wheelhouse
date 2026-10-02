import { toValue, type MaybeRefOrGetter } from "vue";
import { useAppMutation, useAppQuery } from "@/bootstrap/query";
import type { SaveServerRequest } from "@/domain/servers";
import { serversApi } from "@/integration/servers";
import { DeploymentKeys } from "@/application/deployments/DeploymentKeys";
import { ServerKeys } from "./ServerKeys";

export { ServerKeys } from "./ServerKeys";
export { useServerVitals } from "./useServerVitals";

/** The inventory's servers. */
export function useServers() {
  return useAppQuery({
    key: ServerKeys.servers,
    queryFn: ({ signal }) => serversApi.listServers(signal),
  });
}

/** Every target's stored readings of the last `hours`, for trend lines. */
export function useVitalsHistory(hours: MaybeRefOrGetter<number>) {
  return useAppQuery({
    key: () => ServerKeys.history(toValue(hours)),
    queryFn: ({ signal }) => serversApi.getVitalsHistory(toValue(hours), signal),
    meta: { suppressGlobalError: true },
  });
}

/** The operator's confirmed server edits. */
export function useServerChanges() {
  // A server's host and ingress shape every deployment view of its targets.
  const invalidates = () => [ServerKeys.servers, DeploymentKeys.targets];
  const create = useAppMutation({
    mutationFn: (body: SaveServerRequest, { signal }) => serversApi.createServer(body, signal),
    invalidates,
    meta: { suppressGlobalError: true },
  });
  const update = useAppMutation({
    mutationFn: ({ slug, body }: { slug: string; body: SaveServerRequest }, { signal }) =>
      serversApi.updateServer(slug, body, signal),
    invalidates,
    meta: { suppressGlobalError: true },
  });
  const remove = useAppMutation({
    mutationFn: (slug: string, { signal }) => serversApi.deleteServer(slug, signal),
    invalidates,
    meta: { suppressGlobalError: true },
  });
  return {
    create: (body: SaveServerRequest) => create.mutateAsync(body),
    update: (slug: string, body: SaveServerRequest) => update.mutateAsync({ slug, body }),
    remove: (slug: string) => remove.mutateAsync(slug),
  };
}

/** Operations the server form receives. */
export type ServerOperations = ReturnType<typeof useServerChanges>;
