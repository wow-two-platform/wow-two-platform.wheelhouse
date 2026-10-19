import { computed, watch } from "vue";
import { useRoute, useRouter } from "vue-router";
import { useDeploymentTargets } from "@/application/deployments";
import { useLastEnvironment } from "@/application/preferences";
import type { Product } from "@/domain/products";
import { EnvironmentOrder, environmentRank, type DeploymentEnvironment } from "@/domain/targets";

/**
 * The environment a product's Settings or Secrets tab shows and the target that runs it: the one the address names
 * (`?env=`) when the product has it, else the one last chosen on any product page, else the first. Choosing one puts it
 * in the address and remembers it.
 */
export function useEnvironmentTarget(product: () => Product | null) {
  const route = useRoute();
  const router = useRouter();
  const lastEnvironment = useLastEnvironment();
  const targets = useDeploymentTargets();

  const environments = computed(() =>
    [...(product()?.environments ?? [])]
      .map((environment) => environment.name)
      .sort((left, right) => environmentRank(left) - environmentRank(right)),
  );
  const requested = computed(() => {
    const value = route.query["env"];
    const item = Array.isArray(value) ? value[0] : value;
    return typeof item === "string" ? item : null;
  });
  const environment = computed(
    () =>
      environments.value.find((name) => name === requested.value) ??
      environments.value.find((name) => name === lastEnvironment.value) ??
      environments.value[0] ??
      null,
  );
  const target = computed(
    () =>
      (targets.data.value ?? []).find(
        (item) => item.product === product()?.slug && item.environment === environment.value,
      ) ?? null,
  );

  function choose(value: unknown): void {
    if (typeof value !== "string" || value === environment.value) return;
    void router.replace({ path: route.path, query: { ...route.query, env: value } });
  }

  watch(
    environment,
    (value) => {
      if (value && (EnvironmentOrder as readonly string[]).includes(value)) lastEnvironment.value = value as DeploymentEnvironment;
    },
    { immediate: true },
  );

  return { environments, environment, target, targets, choose };
}
