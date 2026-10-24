<script lang="ts">
import type { Product } from "@/domain/products";
import { ReleaseSets } from "@/domain/deployments";

/** A product's Settings tab. */
export interface ProductSettingsProps {
  readonly product: Product;
}
</script>
<script setup lang="ts">
import { SlidersHorizontal } from "lucide-vue-next";
import { EmptyState } from "@wow-two-beta/fe/presentation/display";
import { DataRegion } from "@wow-two-beta/fe/presentation/feedback";
import { ToggleGroup, ToggleInput } from "@wow-two-beta/fe/presentation/forms";
import { PageHeaderActions } from "@wow-two-beta/fe/presentation/layout";
import { useEnvironmentSettings } from "@/application/settings";
import { useInvalidate, useRefresh } from "@/bootstrap/query";
import { SettingsKeys } from "@/application/settings";
import { EnvironmentOrder } from "@/domain/targets";
import { Panel, RefreshButton } from "@/presentation/common/components";
import ApplyBar from "@/presentation/settings/components/ApplyBar.vue";
import SettingsEditor from "@/presentation/settings/components/SettingsEditor.vue";
import VaultStateAlert from "@/presentation/settings/components/VaultStateAlert.vue";
import { useEnvironmentTarget } from "@/presentation/settings/useEnvironmentTarget";

/**
 * The product's plain settings per environment: what its running release declares, with types, and the keys the owner
 * added; each field saves on its own, and the edits apply by deploying the running release again, now or at a set time.
 * The environment sits in the top bar and the address (`?env=`).
 */
defineOptions({ name: "ProductSettings" });
const props = defineProps<ProductSettingsProps>();
defineSlots<{}>();
const invalidate = useInvalidate();
const place = useEnvironmentTarget(() => props.product);
const plan = useEnvironmentSettings(() => place.target.value?.id ?? null);
const refresh = useRefresh(() => invalidate(SettingsKeys.of(place.target.value?.id ?? "")));
</script>
<template>
  <div class="flex flex-col gap-5">
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
      description="Settings belong to an environment; add one from Manage to give this product its settings."
    >
      <template #icon><SlidersHorizontal :size="24" /></template>
    </EmptyState>
    <Panel
      v-else
      title="Settings"
      :description="`${props.product.name} in ${place.environment.value}${plan.data.value?.release ? `, running ${ReleaseSets.text(plan.data.value.release)}` : ''}.`"
    >
      <DataRegion
        :is-first-load="place.targets.isFirstLoad.value || plan.isFirstLoad.value"
        :error="plan.error.value"
        :has-data="Boolean(plan.data.value)"
        :is-empty="!place.targets.isFirstLoad.value && !place.target.value"
        empty-title="No target yet"
        empty-description="This environment has no target to render settings for."
        loading-label="Reading the settings"
      >
        <div v-if="plan.data.value" class="flex flex-col gap-4">
          <VaultStateAlert :vault="plan.data.value.vault" />
          <ApplyBar :plan="plan.data.value" :needs-confirmation="place.target.value?.needsConfirmation ?? false" />
          <SettingsEditor :plan="plan.data.value" :product="props.product.slug" />
        </div>
      </DataRegion>
    </Panel>
  </div>
</template>
