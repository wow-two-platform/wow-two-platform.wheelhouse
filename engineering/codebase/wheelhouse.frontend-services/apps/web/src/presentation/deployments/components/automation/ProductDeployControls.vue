<script setup lang="ts">
import { computed } from 'vue';

import { SwitchField } from '@wow-two-beta/fe/presentation/forms';

import {
  useDeployFreezes,
  useFreezeDeploys,
  useProductAutomation,
  useUnfreezeDeploys,
  useUpdateAutomation,
} from '@/application/deployments';
import { DeployFreezes } from '@/domain/deployments';

/**
 * A product's deploy controls, each saved on its own as it is switched: whether each new release deploys to dev, and
 * freezes of the whole product or of one environment. A product its own CI deploys (Wheelhouse itself) shows the
 * automatic choice off and locked; a portfolio-wide freeze is named, and lifted on the Deployments page.
 */
defineOptions({ name: 'ProductDeployControls' });
const props = defineProps<{ readonly product: string }>();
defineSlots<{}>();

const { choices } = useProductAutomation(() => props.product);
const update = useUpdateAutomation();
const freezes = useDeployFreezes();
const freeze = useFreezeDeploys();
const unfreeze = useUnfreezeDeploys();
const busy = computed(() => freeze.loading.value || unfreeze.loading.value);
const portfolio = computed(() => DeployFreezes.of(freezes.data.value, null, null));
const scopes = [
  { environment: null, label: 'Freeze every deploy of this product' },
  { environment: 'dev', label: 'Freeze dev' },
  { environment: 'prod', label: 'Freeze prod' },
] as const;

function frozen(environment: string | null): boolean {
  return DeployFreezes.of(freezes.data.value, props.product, environment) !== null;
}

function setFrozen(environment: string | null, value: boolean): void {
  const scope = { product: props.product, environment };
  if (value) freeze.mutate(scope);
  else unfreeze.mutate(scope);
}

function setAutoDeployDev(value: boolean): void {
  update.mutate({ product: props.product, change: { autoDeployDev: value } });
}
</script>

<template>
  <section class="flex flex-col gap-2 rounded-md border border-border p-3" aria-label="Deploys">
    <h3 class="text-sm font-medium">Deploys</h3>
    <p v-if="choices.ciDeployed" class="text-xs text-muted-foreground">Its own CI deploys it.</p>
    <SwitchField
      :model-value="choices.autoDeployDev"
      label="Deploy each release to dev"
      size="sm"
      :is-disabled="choices.ciDeployed || update.loading.value"
      @update:model-value="setAutoDeployDev"
    />
    <SwitchField
      v-for="scope in scopes"
      :key="scope.environment ?? 'all'"
      :model-value="frozen(scope.environment)"
      :label="scope.label"
      size="sm"
      :is-disabled="busy"
      @update:model-value="(value: boolean) => setFrozen(scope.environment, value)"
    />
    <p v-if="portfolio" class="text-xs text-warning">{{ DeployFreezes.describe(portfolio) }}</p>
  </section>
</template>
