<script lang="ts">
import type { Product } from "@/domain/products";

/** A product's Secrets tab while deploys still take secrets from the standalone vaults (`Vault:Mode` external). */
export interface LegacyProductSecretsProps {
  readonly product: Product;
}
</script>
<script setup lang="ts">
import { computed, watch } from "vue";
import { RouterLink, useRoute, useRouter } from "vue-router";
import { ExternalLink, KeyRound } from "lucide-vue-next";
import { Button } from "@wow-two-beta/fe/presentation/actions";
import {
  Badge,
  EmptyState,
  TabsGroup,
  TabsGroupList,
  TabsGroupPanel,
  TabsGroupTab,
} from "@wow-two-beta/fe/presentation/display";
import { Alert, DataRegion } from "@wow-two-beta/fe/presentation/feedback";
import { ToggleGroup, ToggleInput } from "@wow-two-beta/fe/presentation/forms";
import { PageHeaderActions } from "@wow-two-beta/fe/presentation/layout";
import { vaultLocation } from "@/application/navigation";
import { useLastEnvironment } from "@/application/preferences";
import { secretBindingsOf, secretEnvironmentOf } from "@/application/product/ProductSecrets";
import {
  SecretKeys,
  useNamespaceCreate,
  useVaultHygiene,
  useVaultNamespaces,
  useVaults,
} from "@/application/secrets";
import { useInvalidate, useRefresh } from "@/bootstrap/query";
import { VaultStatus } from "@/domain/secrets";
import { EnvironmentOrder, type DeploymentEnvironment } from "@/domain/targets";
import { failureReason } from "@/integration/common";
import { Panel, RefreshButton } from "@/presentation/common/components";
import SecretsTable from "@/presentation/secrets/components/SecretsTable.vue";
import TokensTable from "@/presentation/secrets/components/TokensTable.vue";

/**
 * The fallback for one release after the merge: the product's namespace in each environment's standalone vault, with the write-only secrets and product tokens
 * the Secrets page administers, through the same tables. The environment sits in the top bar and the address
 * (`?env=`), and is the one the Overview remembers. A namespace the vault lacks yet is one click away.
 */
defineOptions({ name: "LegacyProductSecrets" });
const props = defineProps<LegacyProductSecretsProps>();
defineSlots<{}>();
const route = useRoute();
const router = useRouter();
const lastEnvironment = useLastEnvironment();
const vaults = useVaults();
const invalidate = useInvalidate();

/** @internal Maps live vault availability to its visible status. */
const VaultTone = { unsealed: "success", sealed: "warning", unreachable: "danger" } as const;

const bindings = computed(() => secretBindingsOf(props.product));
const requested = computed(() => {
  const value = route.query["env"];
  const item = Array.isArray(value) ? value[0] : value;
  return typeof item === "string" ? item : null;
});
const binding = computed(() => secretEnvironmentOf(bindings.value, requested.value, lastEnvironment.value));
const vault = computed(() => (vaults.data.value ?? []).find((item) => item.id === binding.value?.vault) ?? null);
const vaultSlug = computed(() => (vault.value?.status === VaultStatus.Unsealed ? vault.value.id : ""));
const namespaces = useVaultNamespaces(vaultSlug);
const hygiene = useVaultHygiene(vaultSlug);
const creator = useNamespaceCreate(vaultSlug);
const namespace = computed(() => binding.value?.namespace ?? "");
/** Whether the vault holds the environment's namespace; unknown until its namespaces arrive. */
const hasNamespace = computed(() =>
  namespaces.data.value ? namespaces.data.value.some((item) => item.slug === namespace.value) : null,
);
/** The keys overdue for rotation in the namespace. */
const overdueSecrets = computed(
  () =>
    new Set(
      (hygiene.data.value?.overdueSecrets ?? [])
        .filter((secret) => secret.namespace === namespace.value)
        .map((secret) => secret.key),
    ),
);
/** The token warnings scoped to the namespace. */
const tokenFlags = computed(
  () =>
    new Map(
      (hygiene.data.value?.overdueTokens ?? [])
        .filter((token) => token.namespace === namespace.value)
        .map((token) => [token.id, token.reason]),
    ),
);
const refresh = useRefresh(() => invalidate(SecretKeys.vaults));

/** Whether the product has the environment, so its segment can be chosen. @internal */
function hasEnvironment(environment: string): boolean {
  return bindings.value.some((item) => item.environment === environment);
}

/** Chooses an environment from the top bar, in the address and as the remembered one. @internal */
function chooseEnvironment(value: unknown): void {
  if (typeof value !== "string" || value === binding.value?.environment) return;
  void router.replace({ path: route.path, query: { ...route.query, env: value } });
}

/** Creates the environment's namespace in its vault, named after the product and environment. @internal */
async function createNamespace(): Promise<void> {
  if (!binding.value?.namespace) return;
  await creator.create(binding.value.namespace, `${props.product.name} ${binding.value.environment}`);
}

/* The environment shown is the one the next product page and a pinned service open in. */
watch(
  () => binding.value?.environment,
  (environment) => {
    if (environment && (EnvironmentOrder as readonly string[]).includes(environment))
      lastEnvironment.value = environment as DeploymentEnvironment;
  },
  { immediate: true },
);
</script>
<template>
  <div class="flex flex-col gap-5">
    <PageHeaderActions>
      <ToggleGroup
        v-if="bindings.length"
        variant="segmented"
        aria-label="Environment"
        :model-value="binding?.environment ?? null"
        @update:model-value="chooseEnvironment"
      >
        <ToggleInput
          v-for="environment in EnvironmentOrder"
          :key="environment"
          :value="environment"
          size="sm"
          :is-disabled="!hasEnvironment(environment)"
          v-bind="hasEnvironment(environment) ? {} : { title: `No ${environment} environment yet` }"
          >{{ environment }}</ToggleInput
        >
      </ToggleGroup>
      <RefreshButton size="md" icon-only :refreshing="refresh.refreshing.value" @refresh="refresh.refresh" />
    </PageHeaderActions>

    <EmptyState
      v-if="!binding"
      title="No environment yet"
      description="Secrets live in a namespace per environment; add an environment from Manage to give this product one."
    >
      <template #icon><KeyRound :size="24" /></template>
    </EmptyState>
    <EmptyState
      v-else-if="!binding.vault"
      :title="`The ${binding.environment} server runs no vault`"
      description="Add a vault to the server on the Servers page; its namespace for this environment then appears here."
    >
      <template #icon><KeyRound :size="24" /></template>
    </EmptyState>
    <Panel
      v-else
      :title="binding.namespace ?? 'Namespace'"
      :description="`${props.product.name} in ${binding.environment}, on ${vault?.name ?? binding.vault}. Values are write-only.`"
    >
      <template #actions>
        <Badge v-if="vault" :variant="VaultTone[vault.status]">{{ vault.status }}</Badge>
        <Button as-child variant="ghost" tone="neutral" size="sm">
          <RouterLink :to="vaultLocation(binding.vault, binding.namespace)"
            ><ExternalLink :size="14" />Open in vault</RouterLink
          >
        </Button>
      </template>
      <DataRegion
        :is-first-load="vaults.isFirstLoad.value || (Boolean(vaultSlug) && hasNamespace === null && !namespaces.error.value)"
        :error="vaults.error.value ?? namespaces.error.value"
        :has-data="Boolean(vaults.data.value)"
        loading-label="Reading the vault"
      >
        <EmptyState
          v-if="!vault"
          size="sm"
          title="This vault is not available"
          :description="`Wheelhouse does not list ${binding.vault}; check the vault on the Servers page.`"
        />
        <EmptyState
          v-else-if="vault.status !== VaultStatus.Unsealed"
          size="sm"
          :title="`This vault is ${vault.status}`"
          description="Unseal the vault or restore its connection to manage this namespace."
        />
        <EmptyState
          v-else-if="hasNamespace === false"
          size="sm"
          title="No namespace yet"
          :description="`The vault has no ${binding.namespace} namespace; create it to hold this environment's secrets.`"
        >
          <template #actions>
            <Button :is-loading="creator.loading.value" @click="createNamespace">Create {{ binding.namespace }}</Button>
          </template>
        </EmptyState>
        <TabsGroup v-else :key="`${vaultSlug}/${namespace}`" default-value="secrets" class="min-w-0">
          <TabsGroupList aria-label="Namespace administration">
            <TabsGroupTab value="secrets">Secrets</TabsGroupTab>
            <TabsGroupTab value="tokens">Tokens</TabsGroupTab>
          </TabsGroupList>
          <TabsGroupPanel value="secrets" class="pt-5">
            <SecretsTable :vault="vaultSlug" :ns="namespace" :overdue="overdueSecrets" />
          </TabsGroupPanel>
          <TabsGroupPanel value="tokens" class="pt-5">
            <TokensTable :vault="vaultSlug" :ns="namespace" :flags="tokenFlags" />
          </TabsGroupPanel>
        </TabsGroup>
        <Alert
          v-if="creator.error.value"
          class="mt-3"
          severity="danger"
          :description="failureReason(creator.error.value)"
        />
      </DataRegion>
    </Panel>
  </div>
</template>
