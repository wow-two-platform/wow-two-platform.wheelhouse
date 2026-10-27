import { onScopeDispose, shallowRef, toValue, watch, type MaybeRefOrGetter } from "vue";
import { useAppMutation, useAppQuery, useInvalidate } from "@/bootstrap/query";
import { DeploymentKeys } from "@/application/deployments/DeploymentKeys";
import { ServerKeys } from "@/application/servers/ServerKeys";
import { requestLiveVitals } from "@/application/servers/useServerVitals";
import { InfraJobRules, type InfraJobStatus, type StartEnrollmentRequest } from "@/domain/infrastructure";
import { enrollmentApi } from "@/integration/infrastructure";
import { InfrastructureKeys } from "./InfrastructureKeys";

/** Wheelhouse moves an enrolled server's host onto its tailnet address within about ten seconds of success. */
const HOST_SWITCH_MS = 12_000;

/**
 * One server's enrollment, read every two seconds while it is queued or running. When a run seen in flight finishes,
 * the servers, the tailnet status and the vitals are read again — and the servers once more after the host switch.
 */
export function useEnrollment(server: MaybeRefOrGetter<string | null>) {
  const invalidate = useInvalidate();
  /** Mirrors the read's status; the query reads its options before the read exists. @internal */
  const status = shallowRef<InfraJobStatus | undefined>();
  const query = useAppQuery({
    key: () => InfrastructureKeys.enrollment(toValue(server) ?? ""),
    queryFn: ({ signal }) => enrollmentApi.getEnrollment(toValue(server) ?? "", signal),
    enabled: () => Boolean(toValue(server)),
    refetchInterval: () => InfraJobRules.pollInterval(status.value),
    meta: { suppressGlobalError: true },
  });
  let timer: ReturnType<typeof setTimeout> | undefined;
  const refreshServers = () => {
    void invalidate(ServerKeys.servers);
    void invalidate(DeploymentKeys.targets);
  };
  watch(
    () => [query.data.value?.serverId, query.data.value?.status] as const,
    ([id, next], previous) => {
      const before = previous && previous[0] === id ? previous[1] : undefined;
      status.value = next;
      if (!InfraJobRules.hasFinished(before, next)) return;
      refreshServers();
      void invalidate(InfrastructureKeys.tailnet);
      requestLiveVitals();
      void invalidate(ServerKeys.vitals);
      clearTimeout(timer);
      timer = setTimeout(refreshServers, HOST_SWITCH_MS);
    },
    { immediate: true },
  );
  onScopeDispose(() => clearTimeout(timer));
  return query;
}

/** Creates a server's one-time key and starts or reruns its enrollment; each reads the enrollment again. */
export function useEnrollmentChanges() {
  const createKey = useAppMutation({
    mutationFn: (server: string, { signal }) => enrollmentApi.createKey(server, signal),
    invalidates: (server) => [InfrastructureKeys.enrollment(server)],
    meta: { suppressGlobalError: true },
  });
  const start = useAppMutation({
    mutationFn: ({ server, body }: { server: string; body: StartEnrollmentRequest }, { signal }) =>
      enrollmentApi.start(server, body, signal),
    invalidates: ({ server }) => [InfrastructureKeys.enrollment(server)],
    meta: { suppressGlobalError: true },
  });
  return {
    createKey: (server: string) => createKey.mutateAsync(server),
    start: (server: string, body: StartEnrollmentRequest) => start.mutateAsync({ server, body }),
  };
}

/** Operations the enrollment dialog receives. */
export type EnrollmentOperations = ReturnType<typeof useEnrollmentChanges>;
