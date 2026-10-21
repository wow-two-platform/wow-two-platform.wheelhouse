import { toValue, type MaybeRefOrGetter } from "vue";

import { useAppQueries, useAppQuery } from "@/bootstrap/query";
import type { DeploymentTarget } from "@/domain/deployments";
import { deploymentsApi } from "@/integration/deployments";

import { DeploymentKeys } from "../DeploymentKeys";

/** Recent deployments with their last observed outcome. */
export function useDeploymentHistory() {
  return useAppQuery({
    key: DeploymentKeys.history,
    queryFn: ({ signal }) => deploymentsApi.listHistory(signal),
  });
}

/** Deployment outcomes, rollout time and recovery time over the last `days` UTC days. */
export function useDeploymentStats(days: MaybeRefOrGetter<number> = 30) {
  return useAppQuery({
    key: () => DeploymentKeys.stats(toValue(days)),
    queryFn: ({ signal }) => deploymentsApi.getStats(toValue(days), signal),
  });
}

/** The code-owned deployment targets. */
export function useDeploymentTargets() {
  return useAppQuery({
    key: DeploymentKeys.targets,
    queryFn: ({ signal }) => deploymentsApi.listTargets(signal),
  });
}

/** Published release bundles from approved sources. */
export function useReleaseArtifacts() {
  return useAppQuery({
    key: DeploymentKeys.releases,
    queryFn: ({ signal }) => deploymentsApi.listReleases(signal),
  });
}

/** One service's recent container output, read while a viewer is open; it lives only in the session cache. */
export function useServiceLogs(
  target: MaybeRefOrGetter<string | null>,
  service: MaybeRefOrGetter<string | null>,
  tail: MaybeRefOrGetter<number>,
) {
  return useAppQuery({
    key: () => DeploymentKeys.logs(toValue(target) ?? "", toValue(service) ?? "", toValue(tail)),
    queryFn: ({ signal }) =>
      deploymentsApi.getLogs(toValue(target) ?? "", toValue(service) ?? "", toValue(tail), signal),
    enabled: () => Boolean(toValue(target) && toValue(service)),
    meta: { suppressGlobalError: true },
  });
}

/** What one target runs and whether it accepts a deployment. */
export function useTargetState(target: MaybeRefOrGetter<string | null>) {
  return useAppQuery({
    key: () => DeploymentKeys.state(toValue(target) ?? ""),
    queryFn: ({ signal }) =>
      deploymentsApi.getTargetState(toValue(target) ?? "", signal),
    enabled: () => Boolean(toValue(target)),
    meta: { suppressGlobalError: true },
  });
}

/** A product's branches, for choosing a commit to build or deploy to dev; in a product built from several repositories,
 * `repository`'s (its primary one when null). */
export function useProductBranches(
  product: MaybeRefOrGetter<string | null>,
  repository: MaybeRefOrGetter<string | null> = null,
) {
  return useAppQuery({
    key: () => DeploymentKeys.branches(toValue(product) ?? "", toValue(repository) ?? ""),
    queryFn: ({ signal }) =>
      deploymentsApi.listBranches(toValue(product) ?? "", signal, toValue(repository)),
    enabled: () => Boolean(toValue(product)),
    meta: { suppressGlobalError: true },
  });
}

/** A branch's recent commits, each with its build when one exists. */
export function useProductCommits(
  product: MaybeRefOrGetter<string | null>,
  branch: MaybeRefOrGetter<string | null>,
  repository: MaybeRefOrGetter<string | null> = null,
) {
  return useAppQuery({
    key: () =>
      DeploymentKeys.commits(toValue(product) ?? "", toValue(branch) ?? "", toValue(repository) ?? ""),
    queryFn: ({ signal }) =>
      deploymentsApi.listCommits(
        toValue(product) ?? "",
        toValue(branch) ?? "",
        signal,
        toValue(repository),
      ),
    enabled: () => Boolean(toValue(product) && toValue(branch)),
    meta: { suppressGlobalError: true },
  });
}

/** Every given target's state at once, aligned with `targets`; an entry is undefined until it is read. */
export function useTargetStates(targets: MaybeRefOrGetter<readonly DeploymentTarget[]>) {
  return useAppQueries({
    queries: () =>
      toValue(targets).map((target) => ({
        key: DeploymentKeys.state(target.id),
        queryFn: ({ signal }: { signal: AbortSignal }) => deploymentsApi.getTargetState(target.id, signal),
      })),
  });
}
