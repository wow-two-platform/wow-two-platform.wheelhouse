import { shallowRef, toValue, watch, type MaybeRefOrGetter } from "vue";
import { useAppMutation, useAppQuery, useInvalidate } from "@/bootstrap/query";
import { DeploymentKeys } from "@/application/deployments/DeploymentKeys";
import { ServerKeys } from "@/application/servers/ServerKeys";
import { InfraJobRules, UpkeepRules, type InfraJobStatus } from "@/domain/infrastructure";
import { upkeepApi } from "@/integration/infrastructure";
import { InfrastructureKeys } from "./InfrastructureKeys";

/**
 * One server's upkeep runs, read every two seconds while the newest is queued or running. When a run seen in flight
 * finishes, the servers and targets are read again: the console records the kit's ingress and the public address
 * within about ten seconds of success.
 */
export function useUpkeep(server: MaybeRefOrGetter<string | null>, enabled: MaybeRefOrGetter<boolean> = true) {
  const invalidate = useInvalidate();
  /** Mirrors the newest run's status; the query reads its options before the read exists. @internal */
  const status = shallowRef<InfraJobStatus | undefined>();
  const query = useAppQuery({
    key: () => InfrastructureKeys.upkeep(toValue(server) ?? ""),
    queryFn: ({ signal }) => upkeepApi.list(toValue(server) ?? "", signal),
    enabled: () => Boolean(toValue(server)) && toValue(enabled),
    refetchInterval: () => InfraJobRules.pollInterval(status.value),
    meta: { suppressGlobalError: true },
  });
  watch(
    () => [query.data.value?.serverId, UpkeepRules.latest(query.data.value)?.status] as const,
    ([id, next], previous) => {
      const before = previous && previous[0] === id ? previous[1] : undefined;
      status.value = next;
      if (!InfraJobRules.hasFinished(before, next)) return;
      void invalidate(ServerKeys.servers);
      void invalidate(DeploymentKeys.targets);
    },
    { immediate: true },
  );
  return query;
}

/** Starts an upkeep run; its listing is read again. */
export function useUpkeepChanges() {
  const start = useAppMutation({
    mutationFn: (server: string, { signal }) => upkeepApi.start(server, signal),
    invalidates: (server) => [InfrastructureKeys.upkeep(server)],
    meta: { suppressGlobalError: true },
  });
  return { start: (server: string) => start.mutateAsync(server) };
}
