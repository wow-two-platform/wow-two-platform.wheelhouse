<script setup lang="ts">
import { computed } from "vue";
import { Rocket } from "lucide-vue-next";

import { Button } from "@wow-two-beta/fe/presentation/actions";
import { PageHeaderActions } from "@wow-two-beta/fe/presentation/layout";

import { useDeploymentPromotions, useRunningDeployments } from "@/application/deployments";
import { usePortfolio } from "@/application/portfolio";
import { ProductStatus } from "@/domain/products";
import type { DeploymentPromotion } from "@/domain/deployments";

import DeployModal from "../components/DeployModal.vue";
import DeploymentsPanel from "../components/history/DeploymentsPanel.vue";
import RangeStatsTiles from "../components/history/RangeStatsTiles.vue";
import ReadyToPromotePanel from "../components/history/ReadyToPromotePanel.vue";
import RunningNowPanel from "../components/history/RunningNowPanel.vue";
import { useDeployModal } from "../hooks/useDeployModal";
import { useDeploymentViewRoute } from "../hooks/useDeploymentViewRoute";

/**
 * The portfolio's deployments, top to bottom: what runs now, the measures under the shared filter, the releases ready
 * for prod, and the deployments themselves by day or newest first.
 */
defineOptions({ name: "DeploymentsPage" });
const route = useDeploymentViewRoute();
const portfolio = usePortfolio();
const running = useRunningDeployments();
const promotions = useDeploymentPromotions();
const deploy = useDeployModal();
const productNames = computed(
  () => new Map(portfolio.products.products.value.map((product) => [product.slug, product.name] as const)),
);
/** Inactive products stay out of the offers until the operator shows inactive ones, as everywhere else. */
const offers = computed(() => {
  const items = promotions.data.value;
  if (!items || portfolio.showInactive.value) return items;
  const inactive = new Set(
    portfolio.products.products.value.filter((product) => product.status === ProductStatus.Inactive).map((product) => product.slug),
  );
  return items.filter((item) => !inactive.has(item.product));
});

function promote(item: DeploymentPromotion): void {
  deploy.openDeploy({ target: item.prodTargetId, release: item.bundleId });
}
</script>

<template>
  <div class="flex min-w-0 flex-col gap-6">
    <PageHeaderActions>
      <Button variant="solid" tone="primary" @click="deploy.openDeploy()">
        <template #leading><Rocket :size="16" /></template>Deploy
      </Button>
    </PageHeaderActions>
    <RunningNowPanel v-if="running.data.value?.length" :items="running.data.value" :product-names="productNames" />
    <RangeStatsTiles :filter="route.filter.value" />
    <ReadyToPromotePanel
      :items="offers"
      :product-names="productNames"
      :is-first-load="promotions.isFirstLoad.value"
      :error="promotions.error.value"
      @promote="promote"
      @retry="promotions.refetch()"
    />
    <DeploymentsPanel />
    <DeployModal
      :key="deploy.session.value"
      :open="deploy.open.value"
      :selection="deploy.selection.value"
      @update:open="deploy.onOpenChange"
    />
  </div>
</template>
