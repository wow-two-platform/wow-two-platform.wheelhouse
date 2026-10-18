<script setup lang="ts">
import { SwitchField } from '@wow-two-beta/fe/presentation/forms';
import { useProductAutomation, useUpdateAutomation } from '@/application/deployments';

/**
 * A product's deploy controls, each saved on its own as it is switched: whether each new release deploys to dev. A
 * product its own CI deploys (Wheelhouse itself) shows them off and locked.
 */
defineOptions({ name: 'ProductDeployControls' });
const props = defineProps<{ readonly product: string }>();
defineSlots<{}>();

const { choices } = useProductAutomation(() => props.product);
const update = useUpdateAutomation();

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
  </section>
</template>
