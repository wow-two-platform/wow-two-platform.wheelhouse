<script lang="ts">
import type { Product } from "@/domain/products";

/** A product's Secrets tab. */
export interface ProductSecretsProps {
  readonly product: Product;
}
</script>
<script setup lang="ts">
import { defineAsyncComponent } from "vue";
import { KeyRound } from "lucide-vue-next";
import { EmptyState } from "@wow-two-beta/fe/presentation/display";
import { DataRegion } from "@wow-two-beta/fe/presentation/feedback";
import { ToggleGroup, ToggleInput } from "@wow-two-beta/fe/presentation/forms";
import { PageHeaderActions } from "@wow-two-beta/fe/presentation/layout";
import { SettingsKeys, useEnvironmentSettings } from "@/application/settings";
import { useInvalidate, useRefresh } from "@/bootstrap/query";
import { EnvironmentOrder } from "@/domain/targets";
import { Panel, RefreshButton } from "@/presentation/common/components";
import ApplyBar from "@/presentation/settings/components/ApplyBar.vue";
import SecretsEditor from "@/presentation/settings/components/SecretsEditor.vue";
import VaultStateAlert from "@/presentation/settings/components/VaultStateAlert.vue";
import { useEnvironmentTarget } from "@/presentation/settings/useEnvironmentTarget";

/* While deploys still take secrets from the standalone vaults, the tab is the old namespace view. @internal */
const LegacyProductSecrets = defineAsyncComponent(() => import("./LegacyProductSecrets.vue"));

/**
 * The product's secrets per environment, in the built-in Secrets Vault: each declared or added key with its version,
 * author, fingerprint and expiry, never a value; new versions, history and restores, and the apply that renders them.
 * The environment sits in the top bar and the address (`?env=`).
 */
defineOptions({ name: "ProductSecrets" });
const props = defineProps<ProductSecretsProps>();
defineSlots<{}>();
const invalidate = useInvalidate();
const place = useEnvironmentTarget(() => props.product);
const plan = useEnvironmentSettings(() => place.target.value?.id ?? null);
const refresh = useRefresh(() => invalidate(SettingsKeys.of(place.target.value?.id ?? "")));
</script>
<template>
  <LegacyProductSecrets v-if="plan.data.value?.vault?.mode === 'external'" :product="props.product" />
  <div v-else class="flex flex-col gap-5">
    <PageHeaderActions>
      <ToggleGroup
        v-if="place.environments.value.length"
        variant="segmented"
        aria-label="Environment"
        :model-value="place.environment.value"
        @update:model-value="place.choose"
      >
        <ToggleInput
          v-for="environment in EnvironmentOrder"
          :key="environment"
          :value="environment"
          size="sm"
          :is-disabled="!place.environments.value.includes(environment)"
          >{{ environment }}</ToggleInput
        >
      </ToggleGroup>
      <RefreshButton size="md" icon-only :refreshing="refresh.refreshing.value" @refresh="refresh.refresh" />
    </PageHeaderActions>
    <EmptyState
      v-if="!place.environment.value"
      title="No environment yet"
      description="Secrets belong to an environment; add one from Manage to give this product its secrets."
    >
      <template #icon><KeyRound :size="24" /></template>
    </EmptyState>
    <Panel
      v-else
      title="Secrets"
      :description="`${props.product.name} in ${place.environment.value}, in the built-in vault. Values are write-only.`"
    >
      <DataRegion
        :is-first-load="place.targets.isFirstLoad.value || plan.isFirstLoad.value"
        :error="plan.error.value"
        :has-data="Boolean(plan.data.value)"
        :is-empty="!place.targets.isFirstLoad.value && !place.target.value"
        empty-title="No target yet"
        empty-description="This environment has no target to render secrets for."
        loading-label="Reading the secrets"
      >
        <div v-if="plan.data.value" class="flex flex-col gap-4">
          <VaultStateAlert :vault="plan.data.value.vault" />
          <ApplyBar :plan="plan.data.value" :needs-confirmation="place.target.value?.needsConfirmation ?? false" />
          <SecretsEditor :plan="plan.data.value" :product="props.product.slug" />
        </div>
      </DataRegion>
    </Panel>
  </div>
</template>
