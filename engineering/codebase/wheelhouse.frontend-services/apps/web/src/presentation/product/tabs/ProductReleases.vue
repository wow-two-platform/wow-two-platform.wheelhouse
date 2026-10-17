<script lang="ts">
/** A product's Releases tab. */
export interface ProductReleasesProps {
  /** The product's slug, which its releases name. */
  readonly product: string;
}
</script>
<script setup lang="ts">
import { Rocket } from "lucide-vue-next";
import { Button } from "@wow-two-beta/fe/presentation/actions";
import { PageHeaderActions } from "@wow-two-beta/fe/presentation/layout";
import { DeployModal, useDeployModal } from "@/presentation/deployments";
import ReleaseCatalogPanel from "@/presentation/deployments/components/ReleaseCatalogPanel.vue";

/**
 * The product's published releases and commit builds, newest first; each deploys from its row, choosing the
 * environment in the deployment dialog.
 */
defineOptions({ name: "ProductReleases" });
const props = defineProps<ProductReleasesProps>();
defineSlots<{}>();
const deploy = useDeployModal();
</script>
<template>
  <div>
    <PageHeaderActions>
      <Button @click="deploy.openDeploy()"><template #leading><Rocket :size="16" /></template>Deploy</Button>
    </PageHeaderActions>
    <ReleaseCatalogPanel :product="props.product" @deploy="deploy.openDeploy({ target: '', release: $event })" />
    <DeployModal
      :key="deploy.session.value"
      :open="deploy.open.value"
      :selection="deploy.selection.value"
      @update:open="deploy.onOpenChange"
    />
  </div>
</template>
