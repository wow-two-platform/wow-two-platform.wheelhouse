import { computed } from "vue";
import { useDeploymentHistory, useDeploymentStats, useDeploymentTargets } from "@/application/deployments";
import { useServerVitals } from "@/application/servers";
import type { DeploymentJob } from "@/domain/deployments";
import { EnvironmentHealthRules, type EnvironmentReading } from "@/domain/overview";

/**
 * Each product's dev and prod health and its newest deployment, from the targets, vitals, deployment stats and
 * history the console already reads. Nothing here asks the host for more.
 */
export function useProductReadings() {
  const targets = useDeploymentTargets();
  const vitals = useServerVitals();
  const stats = useDeploymentStats();
  const history = useDeploymentHistory();
  const byProduct = computed(() => {
    const readings = { vitals: vitals.data.value, stats: stats.data.value };
    const all = targets.data.value ?? [];
    const map = new Map<string, { environments: EnvironmentReading[]; lastDeploy: DeploymentJob | null }>();
    for (const slug of new Set(all.map((target) => target.product))) {
      const own = all.filter((target) => target.product === slug);
      map.set(slug, {
        environments: EnvironmentHealthRules.ofProduct(slug, own, readings),
        lastDeploy: EnvironmentHealthRules.lastDeploy(
          own.map((target) => target.id),
          history.data.value,
        ),
      });
    }
    return map;
  });

  return {
    /** A product's environments in promotion order; a product without targets has every one missing. */
    environmentsOf: (slug: string): EnvironmentReading[] =>
      byProduct.value.get(slug)?.environments ??
      EnvironmentHealthRules.ofProduct(slug, [], { vitals: vitals.data.value }),
    lastDeployOf: (slug: string): DeploymentJob | null => byProduct.value.get(slug)?.lastDeploy ?? null,
    /** True until the history first answers; a card's last deploy draws a placeholder meanwhile. */
    historyLoading: computed(() => history.data.value === undefined && !history.error.value),
    refresh: () => Promise.all([targets.refetch(), vitals.refetch(), stats.refetch(), history.refetch()]),
  };
}
