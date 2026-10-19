import { computed, shallowRef, watch } from "vue";

import { useAppQuery } from "@/bootstrap/query";
import type { SelfDeployment } from "@/domain/deployments";
import { deploymentsApi } from "@/integration/deployments";

import { DeploymentKeys } from "../DeploymentKeys";

/** @internal How often a self-rollout still under way is read again. */
const PollMilliseconds = 10_000;

/**
 * This console's own newest rollout, read once the console loads: after a self-promotion the new console shows how it
 * ended. While it is still under way (the host runner probes the new console and tidies up) it is read again.
 */
export function useSelfDeployment() {
  /** Mirrors the reading's pending state; the query reads its options before the reading exists. @internal */
  const pending = shallowRef(false);
  const query = useAppQuery({
    key: DeploymentKeys.self,
    queryFn: ({ signal }) => deploymentsApi.getSelfDeployment(signal),
    refetchInterval: () => (pending.value ? PollMilliseconds : false),
    meta: { suppressGlobalError: true },
  });
  const reading = computed<SelfDeployment | null>(() => query.data.value ?? null);
  watch(
    () => reading.value?.latest?.outcome,
    (outcome) => {
      pending.value = outcome === "pending";
    },
    { immediate: true },
  );
  return { reading } as const;
}
