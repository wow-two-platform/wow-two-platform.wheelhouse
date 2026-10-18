import { useAppMutation } from "@/bootstrap/query";
import { TopologyKeys } from "@/application/topology";
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

/** Submits a release to a target; the outcome is followed separately. */
export function useStartDeployment() {
  return useAppMutation({
    mutationFn: (
      {
        target,
        release,
        confirm,
        skipDevPass,
      }: {
        target: string;
        release: string;
        confirm?: string;
        skipDevPass?: boolean;
      },
      { signal },
    ) =>
      deploymentsApi.startDeployment(
        target,
        release,
        confirm,
        skipDevPass,
        signal,
      ),
    invalidates: ({ target }) => [
      DeploymentKeys.history,
      DeploymentKeys.state(target),
    ],
    meta: { suppressGlobalError: true },
  });
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
      { product, commit }: { product: string; commit: string },
      { signal },
    ) => deploymentsApi.requestBuild(product, commit, signal),
    invalidates: () => [DeploymentKeys.releases, DeploymentKeys.commitsAll],
    meta: { suppressGlobalError: true },
  });
}
