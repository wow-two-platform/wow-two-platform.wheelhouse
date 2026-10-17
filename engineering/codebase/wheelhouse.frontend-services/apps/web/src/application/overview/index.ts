import { computed } from "vue";
import { useDeploymentStats } from "@/application/deployments";
import { useVaults, useVaultsHygiene } from "@/application/secrets";
import { useServerVitals } from "@/application/servers";
import { AttentionRules } from "@/domain/overview";

/** What needs the operator across every product: unreachable hosts, failing containers, rotation debt. */
export function usePortfolioAttention() {
  const vitals = useServerVitals();
  const stats = useDeploymentStats();
  const vaults = useVaults();
  const hygiene = useVaultsHygiene(() => vaults.data.value ?? []);
  const items = computed(() =>
    AttentionRules.sort([
      ...(vitals.data.value ? AttentionRules.fromVitals(vitals.data.value) : []),
      ...(stats.data.value ? AttentionRules.fromStats(stats.data.value) : []),
      ...(vaults.data.value ?? []).flatMap((vault) =>
        AttentionRules.fromVault(vault, hygiene.byVault.value.get(vault.id)),
      ),
    ]),
  );
  const incomplete = computed(() =>
    Boolean(vitals.error.value || stats.error.value || vaults.error.value || hygiene.errors.value.length),
  );
  const loading = computed(
    () => vitals.loading.value || stats.loading.value || vaults.loading.value || hygiene.loading.value,
  );
  return { items, incomplete, loading };
}
