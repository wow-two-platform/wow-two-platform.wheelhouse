<script setup lang="ts">
import { computed } from "vue";
import { Rocket } from "lucide-vue-next";

import { Button } from "@wow-two-beta/fe/presentation/actions";
import { PageHeaderActions } from "@wow-two-beta/fe/presentation/layout";

import { buildEntityTree, useDeploymentPromotions, useRunningDeployments } from "@/application/deployments";
import { usePortfolio } from "@/application/portfolio";
import { ProductStatus } from "@/domain/products";
import type { DeploymentPromotion } from "@/domain/deployments";

import DeployModal from "../components/DeployModal.vue";
import DeploymentFilterScope from "../components/history/DeploymentFilterScope.vue";
import DeploymentsPanel from "../components/history/DeploymentsPanel.vue";
import RangeStatsTiles from "../components/history/RangeStatsTiles.vue";
import ReadyToPromotePanel from "../components/history/ReadyToPromotePanel.vue";
import RunningNowPanel from "../components/history/RunningNowPanel.vue";
import { useDeployModal } from "../hooks/useDeployModal";
import { useDeploymentViewRoute } from "../hooks/useDeploymentViewRoute";

/**
 * The portfolio's deployments, top to bottom: what runs now and the releases ready for prod — both portfolio-wide and
 * shown only when there is one — then the filter, and under it the measures and the deployments by day or newest
 * first. The filter reaches only what lies below it.
 */
defineOptions({ name: "DeploymentsPage" });
const route = useDeploymentViewRoute();
const portfolio = usePortfolio();
const running = useRunningDeployments();
const promotions = useDeploymentPromotions();
const deploy = useDeployModal();
const tree = computed(() => buildEntityTree(portfolio.groups.value, portfolio.showInactive.value));
const productNames = computed(
  () => new Map(portfolio.products.products.value.map((product) => [product.slug, product.name] as const)),
);
/** The products whose rows stay hidden until the owner shows inactive ones, as everywhere else. */
const hiddenProducts = computed(
  () =>
    new Set(
      portfolio.showInactive.value
        ? []
        : portfolio.products.products.value.filter((product) => product.status === ProductStatus.Inactive).map((product) => product.slug),
    ),
);
const offers = computed(() => promotions.data.value?.filter((item) => !hiddenProducts.value.has(item.product)));

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
    <ReadyToPromotePanel
      v-if="offers?.length || promotions.error.value"
      :items="offers"
      :product-names="productNames"
      :is-first-load="promotions.isFirstLoad.value"
      :error="promotions.error.value"
      @promote="promote"
      @retry="promotions.refetch()"
    />
    <div class="flex min-w-0 flex-col gap-4">
      <DeploymentFilterScope :filter="route.filter.value" :tree="tree" @change="route.setFilter" />
      <RangeStatsTiles :filter="route.filter.value" />
      <DeploymentsPanel :has-filter-bar="false" />
    </div>
    <DeployModal
      :key="deploy.session.value"
      :open="deploy.open.value"
      :selection="deploy.selection.value"
      @update:open="deploy.onOpenChange"
    />
  </div>
</template>
