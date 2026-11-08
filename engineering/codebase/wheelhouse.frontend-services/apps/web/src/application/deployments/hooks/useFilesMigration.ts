import { toValue, type MaybeRefOrGetter } from "vue";

import { useAppMutation, useAppQuery } from "@/bootstrap/query";
import {
  DeploymentFilters,
  DefaultPreset,
  EntityKeys,
  type DeploymentItem,
} from "@/domain/deployments";
import { FilesMigrations, type FilesMigrationAsk } from "@/domain/deployments/controls";
import { deploymentsApi } from "@/integration/deployments";
import { deploymentHistoryApi } from "@/integration/deployments/historyApi";

import { DeploymentKeys } from "../DeploymentKeys";

/** How many of a product's newest jobs the import prefill scans. */
const ImportScanLimit = 100;

/** The sources the manifests of the release a dev target runs list and the Files targets of its environment, read when
 * `enabled`; its refusals (prod, no manifest, nothing deployed) show in the dialog. */
export function useFilesMigrationManifest(target: MaybeRefOrGetter<string | null>, enabled: MaybeRefOrGetter<boolean> = true) {
  return useAppQuery({
    key: () => DeploymentKeys.filesManifest(toValue(target) ?? ""),
    queryFn: ({ signal }) => deploymentsApi.filesMigrationManifest(toValue(target) ?? "", signal),
    enabled: () => Boolean(toValue(target)) && toValue(enabled),
    meta: { suppressGlobalError: true },
  });
}

/** The import id of the newest files-migration job of a target that kept one, among its product's newest jobs of the
 * half year (every attempt, the cancelled and failed included); null when none did. */
export function useLatestFilesImport(
  target: MaybeRefOrGetter<string | null>,
  product: MaybeRefOrGetter<string | null>,
  enabled: MaybeRefOrGetter<boolean> = true,
) {
  return useAppQuery({
    key: () => DeploymentKeys.filesImport(toValue(target) ?? ""),
    queryFn: async ({ signal }) => {
      const params = DeploymentFilters.params(
        { entities: [EntityKeys.product(toValue(product) ?? "")], range: { kind: "preset", preset: DefaultPreset } },
        new Date(),
        { view: { everyAttempt: true, timeZone: "UTC" } },
      );
      const page = await deploymentHistoryApi.listPage(params, null, ImportScanLimit, signal);
      if (!page.ok) return page;
      return { ok: true as const, value: FilesMigrations.latestImport(page.value.items as DeploymentItem[], toValue(target) ?? "") };
    },
    enabled: () => Boolean(toValue(target) && toValue(product)) && toValue(enabled),
    meta: { suppressGlobalError: true },
  });
}

/** Queues a plan, push or verify of a dev target's legacy files; the job is followed with `useDeploymentOutcome`. */
export function useStartFilesMigration() {
  return useAppMutation({
    mutationFn: ({ target, ask }: { target: string; ask: FilesMigrationAsk }, { signal }) =>
      deploymentsApi.startFilesMigration(target, FilesMigrations.body(ask), signal),
    invalidates: ({ target }) => [DeploymentKeys.history, DeploymentKeys.state(target), DeploymentKeys.filesImport(target)],
    meta: { suppressGlobalError: true },
  });
}

/** Cancels a running files migration job: the mover's container is stopped and the job reads cancelled. */
export function useCancelFilesMigration() {
  return useAppMutation({
    mutationFn: ({ target, job }: { target: string; job: string }, { signal }) =>
      deploymentsApi.cancelControl(target, job, signal),
    invalidates: ({ job }) => [DeploymentKeys.outcome(job), DeploymentKeys.history],
    meta: { suppressGlobalError: true },
  });
}
