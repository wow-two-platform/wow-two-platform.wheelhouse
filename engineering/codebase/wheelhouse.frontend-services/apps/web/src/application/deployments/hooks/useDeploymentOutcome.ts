import {
  computed,
  shallowRef,
  toValue,
  watch,
  type MaybeRefOrGetter,
} from "vue";

import { requestLiveVitals, ServerKeys } from "@/application/servers";
import { TopologyKeys } from "@/application/topology";
import { useAppQuery, useInvalidate } from "@/bootstrap/query";
import { DeploymentExtensions, type JobStatus } from "@/domain/deployments";
import { deploymentsApi } from "@/integration/deployments";

import { DeploymentKeys } from "../DeploymentKeys";

const PollMilliseconds = 3000;

/**
 * Follows a reactive submission ID until its target records a final outcome. `listed` is the status the caller already
 * shows for the job — a history row's, or `queued` for a job just submitted. Only a job that finishes after it was seen
 * pending refreshes the history, statistics, vitals, state and topology beside it; opening a job that had already
 * finished refetches none of them.
 */
export function useDeploymentOutcome(
  id: MaybeRefOrGetter<string | null>,
  listed: MaybeRefOrGetter<JobStatus | null | undefined> = null,
) {
  const invalidate = useInvalidate();
  /** Mirrors `pending` below; the query reads its options before `pending` exists. @internal */
  const polling = shallowRef(false);
  const outcome = useAppQuery({
    key: () => DeploymentKeys.outcome(toValue(id) ?? ""),
    queryFn: ({ signal }) =>
      deploymentsApi.getOutcome(toValue(id) ?? "", signal),
    enabled: () => Boolean(toValue(id)),
    refetchInterval: () => (polling.value ? PollMilliseconds : false),
    meta: { suppressGlobalError: true },
  });
  const pending = computed(
    () =>
      outcome.data.value !== undefined &&
      DeploymentExtensions.isPending(outcome.data.value.status),
  );

  watch(
    pending,
    (active) => {
      polling.value = active;
    },
    { immediate: true },
  );

  /** The last status this hook read for a job; a new job starts from the caller's listed status. @internal */
  let last: { readonly job: string; readonly status: JobStatus } | null = null;

  watch(
    () =>
      [
        outcome.data.value?.id,
        outcome.data.value?.status,
        outcome.data.value?.targetId,
      ] as const,
    ([job, status, target]) => {
      if (!job || !status) return;
      const before = last?.job === job ? last.status : toValue(listed);
      last = { job, status };
      if (
        DeploymentExtensions.isPending(status) ||
        !before ||
        !DeploymentExtensions.isPending(before)
      )
        return;
      void invalidate(DeploymentKeys.history);
      void invalidate(DeploymentKeys.statsAll);
      requestLiveVitals();
      void invalidate(ServerKeys.vitals);
      if (target) {
        void invalidate(DeploymentKeys.state(target));
        void invalidate(TopologyKeys.target(target));
      }
    },
    { immediate: true },
  );

  return { ...outcome, pending };
}
