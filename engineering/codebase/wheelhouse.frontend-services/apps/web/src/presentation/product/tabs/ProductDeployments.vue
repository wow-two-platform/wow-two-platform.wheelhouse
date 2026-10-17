<script lang="ts">
/** A product's Deployments tab. */
export interface ProductDeploymentsProps {
  /** The product's slug, which locks the deployments filter. */
  readonly product: string;
}
</script>
<script setup lang="ts">
import { Rocket } from "lucide-vue-next";
import { Button } from "@wow-two-beta/fe/presentation/actions";
import { PageHeaderActions } from "@wow-two-beta/fe/presentation/layout";
import { DeployModal, DeploymentsPanel, useDeployModal } from "@/presentation/deployments";

/**
 * The product's rollouts: the Deployments page's panel with its entity filter locked to this product, so the calendar,
 * the list, the status filter and the range work as they do there.
 */
defineOptions({ name: "ProductDeployments" });
const props = defineProps<ProductDeploymentsProps>();
defineSlots<{}>();
const deploy = useDeployModal();
</script>
<template>
  <div class="min-w-0">
    <PageHeaderActions>
      <Button @click="deploy.openDeploy()"><template #leading><Rocket :size="16" /></template>Deploy</Button>
    </PageHeaderActions>
    <DeploymentsPanel :locked-product="props.product" />
    <DeployModal
      :key="deploy.session.value"
      :open="deploy.open.value"
      :selection="deploy.selection.value"
      @update:open="deploy.onOpenChange"
    />
  </div>
</template>
