import { useAppMutation, useInvalidate } from "@/bootstrap/query";
import { TopologyKeys } from "@/application/topology";
import { ServerKeys } from "@/application/servers/ServerKeys";
import { requestLiveVitals } from "@/application/servers/useServerVitals";
import { DeploymentExtensions, type DeploymentJob } from "@/domain/deployments";
import type { ControlOperation } from "@/domain/deployments/controls";
import { deploymentsApi } from "@/integration/deployments";

import { DeploymentKeys } from "../DeploymentKeys";

/** A read-only readiness check, run on demand because it reaches the target. */
export function useTargetCheck() {
  return useAppMutation({
    mutationFn: (
      { target, release }: { target: string; release?: string },
      { signal },
    ) => deploymentsApi.checkTarget(target, release, signal),
    meta: { suppressGlobalError: true },
  });
}

/** Check now: reads the target's databases against the release it runs (`migrate check`, write-free, up to about 90
 * seconds), then the target's state again, which carries the sets the check read. Its failures show in place. */
export function useMigrationsCheck() {
  return useAppMutation({
    mutationFn: ({ target }: { target: string }, { signal }) =>
      deploymentsApi.checkMigrations(target, signal),
    invalidates: ({ target }) => [DeploymentKeys.state(target)],
    meta: { suppressGlobalError: true },
  });
}

/** Submits a release to a target; the outcome is followed separately. */
export function useStartDeployment() {
  return useAppMutation({
    mutationFn: (
      {
        target,
        release,
        confirm,
        skipDevPass,
        reason,
        overrideFreeze,
      }: {
        target: string;
        release: string;
        confirm?: string;
        skipDevPass?: boolean;
        /** The owner's one-line why, kept on the job; needed to override a freeze. */
        reason?: string;
        /** Deploys although a freeze holds the target. */
        overrideFreeze?: boolean;
      },
      { signal },
    ) =>
      deploymentsApi.startDeployment(
        target,
        release,
        confirm,
        skipDevPass,
        signal,
        { reason, overrideFreeze },
      ),
    invalidates: ({ target }) => [
      DeploymentKeys.history,
      DeploymentKeys.state(target),
    ],
    meta: { suppressGlobalError: true },
  });
}

/** Stops, starts or restarts the release a target runs — `service`, or the whole environment without one; the job is
 * followed like a deploy. Prod needs `confirm` (its target ID, typed) and a `reason`. */
export function useTargetControl() {
  const invalidate = useInvalidate();
  const mutation = useAppMutation({
    mutationFn: (
      {
        target,
        operation,
        service,
        reason,
        confirm,
      }: {
        target: string;
        operation: ControlOperation;
        service?: string | null | undefined;
        reason?: string | undefined;
        confirm?: string | undefined;
      },
      { signal },
    ) => deploymentsApi.control(target, { operation, service, reason, confirm }, signal),
    invalidates: ({ target }) => refreshed(target),
    meta: { suppressGlobalError: true },
  });

  /** Follows a queued control on its target until it ends, then refreshes what it changed: the job as it ended, or
   * null when it still runs past the wait. A failure the target recorded — a refused rule, an unhealthy start — comes
   * back with its reason, for the dialog that queued it to show. */
  async function follow(id: string, target: string): Promise<DeploymentJob | null> {
    const until = Date.now() + ControlFollowMs;
    while (Date.now() < until) {
      await new Promise((resolve) => setTimeout(resolve, ControlPollMs));
      const read = await deploymentsApi.getOutcome(id);
      if (read.ok && !DeploymentExtensions.isPending(read.value.status)) {
        // The control changed the containers: the next snapshot reads the hosts now.
        requestLiveVitals();
        for (const key of refreshed(target)) void invalidate(key);
        return read.value;
      }
    }
    return null;
  }

  return { ...mutation, follow };
}

/** How often a queued control is read back, and for how long: past the longest health wait a start may take. */
const ControlPollMs = 2000;
const ControlFollowMs = 6 * 60 * 1000;

/** What a control changes: the target's state, the history, the vitals and the service map. @internal */
function refreshed(target: string) {
  return [DeploymentKeys.state(target), DeploymentKeys.history, ServerKeys.vitals, TopologyKeys.target(target)];
}

/** Records the owner's reconciliation of a locked target. */
export function useReconcileTarget() {
  return useAppMutation({
    mutationFn: (
      { target, job }: { target: string; job: string },
      { signal },
    ) => deploymentsApi.reconcile(target, job, signal),
    invalidates: ({ target }) => [
      DeploymentKeys.state(target),
      DeploymentKeys.history,
      TopologyKeys.target(target),
    ],
    meta: { suppressGlobalError: true },
  });
}

/** Requests a build of a commit that has none; the catalog lists it once the workflow finishes. */
export function useRequestBuild() {
  return useAppMutation({
    mutationFn: (
      { product, commit, repository }: { product: string; commit: string; repository?: string | null },
      { signal },
    ) => deploymentsApi.requestBuild(product, commit, signal, repository),
    invalidates: () => [DeploymentKeys.releases, DeploymentKeys.commitsAll],
    meta: { suppressGlobalError: true },
  });
}
