<script lang="ts">
/** A product's Deployments tab. */
export interface ProductDeploymentsProps {
  /** The product's slug, which locks the deployments filter and keeps the catalog to its releases. */
  readonly product: string;
}
</script>
<script setup lang="ts">
import { Rocket } from "lucide-vue-next";
import { Button } from "@wow-two-beta/fe/presentation/actions";
import { PageHeaderActions } from "@wow-two-beta/fe/presentation/layout";
import { DeployModal, DeploymentsPanel, useDeployModal } from "@/presentation/deployments";
import ReleaseCatalogPanel from "@/presentation/deployments/components/ReleaseCatalogPanel.vue";

/**
 * The product's deployments in one place: its dev and prod history — the Deployments page's panel with its entity
 * filter locked to this product, so the calendar, the list, the attempts toggle and the range work as they do there —
 * then the releases and commit builds it can deploy, each deploying from its row with the environment chosen in the
 * deployment dialog. A release link (`?view=list&release=…&env=…`) opens the history's list on that release.
 */
defineOptions({ name: "ProductDeployments" });
const props = defineProps<ProductDeploymentsProps>();
defineSlots<{}>();
const deploy = useDeployModal();
</script>
<template>
  <div class="flex min-w-0 flex-col gap-5">
    <PageHeaderActions>
      <Button @click="deploy.openDeploy()"><template #leading><Rocket :size="16" /></template>Deploy</Button>
    </PageHeaderActions>
    <DeploymentsPanel :locked-product="props.product" />
    <ReleaseCatalogPanel :product="props.product" @deploy="deploy.openDeploy({ target: '', release: $event })" />
    <DeployModal
      :key="deploy.session.value"
      :open="deploy.open.value"
      :selection="deploy.selection.value"
      @update:open="deploy.onOpenChange"
    />
  </div>
</template>
