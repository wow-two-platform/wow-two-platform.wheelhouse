<script setup lang="ts">
import { computed, nextTick, ref, shallowRef } from "vue";
import { Rocket } from "lucide-vue-next";

import { notify } from "@wow-two-beta/fe/feedback";
import { Button } from "@wow-two-beta/fe/presentation/actions";
import { PageHeaderActions } from "@wow-two-beta/fe/presentation/layout";

import {
  buildEntityTree,
  useBuildFailures,
  useCancelFleet,
  useFleetRollouts,
  useDeploymentIssues,
  useDeploymentPromotions,
  useRunningDeployments,
  useTargetState,
} from "@/application/deployments";
import { usePortfolio } from "@/application/portfolio";
import { ProductStatus } from "@/domain/products";
import {
  FleetRollouts,
  FleetStatus,
  TargetCondition,
  type DeploymentIssue,
  type DeploymentPromotion,
} from "@/domain/deployments";

import DeployModal from "../components/DeployModal.vue";
import FleetPromoteModal from "../components/automation/FleetPromoteModal.vue";
import FleetRolloutsPanel from "../components/history/FleetRolloutsPanel.vue";
import ReconcileModal from "../components/ReconcileModal.vue";
import DeploymentFilterScope from "../components/history/DeploymentFilterScope.vue";
import DeploymentsPanel from "../components/history/DeploymentsPanel.vue";
import IssuesPanel from "../components/history/IssuesPanel.vue";
import RangeStatsTiles from "../components/history/RangeStatsTiles.vue";
import ReadyToPromotePanel from "../components/history/ReadyToPromotePanel.vue";
import RunningNowPanel from "../components/history/RunningNowPanel.vue";
import PortfolioFreezeToggle from "../components/automation/PortfolioFreezeToggle.vue";
import { useDeployModal } from "../hooks/useDeployModal";
import { useDeploymentViewRoute } from "../hooks/useDeploymentViewRoute";

/**
 * The portfolio's deployments, top to bottom: the targets that need attention, what runs now and the releases ready
 * for prod — each portfolio-wide and shown only when there is one — then the filter, and under it the measures and the
 * deployments by day or newest first. The filter reaches only what lies below it.
 */
defineOptions({ name: "DeploymentsPage" });
const route = useDeploymentViewRoute();
const portfolio = usePortfolio();
const issues = useDeploymentIssues();
const running = useRunningDeployments();
const promotions = useDeploymentPromotions();
const deploy = useDeployModal();
const fleets = useFleetRollouts();
const cancelFleet = useCancelFleet();
/** The prod targets the running fleet still holds: their Ready to promote rows wait for it. */
const heldTargets = computed(() => FleetRollouts.heldTargets(fleets.active.value));
/** The fleets that ended in the last day, newest first. */
const recentFleets = computed(() =>
  (fleets.fleets.data.value ?? []).filter(
    (fleet) => fleet.status !== FleetStatus.Running && Date.now() - Date.parse(fleet.completedAt ?? fleet.createdAt) < 24 * 3600_000,
  ),
);
const fleetItems = shallowRef<DeploymentPromotion[]>([]);
const fleetOpen = ref(false);
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
const builds = useBuildFailures();
const shownBuilds = computed(() => (builds.data.value ?? []).filter((item) => !hiddenProducts.value.has(item.product)));
const shownIssues = computed(() =>
  issues.data.value?.filter((item) => !item.deployment.product || !hiddenProducts.value.has(item.deployment.product)),
);

/** The target whose reconcile dialog is open or about to open; its state names the remote rollout to acknowledge. */
const reconcileTarget = ref<string | null>(null);
const reconcileState = useTargetState(reconcileTarget);
const reconcileOpen = ref(false);
/** The target whose state is being read, while its Reconcile button waits. */
const checkingTarget = ref<string | null>(null);

function redeploy(issue: DeploymentIssue): void {
  const { targetId, bundleId } = issue.deployment;
  if (targetId && bundleId) deploy.openDeploy({ target: targetId, release: bundleId });
}

/**
 * Reads the target's own state first: the history only suggests a lock, and the dialog acknowledges the exact remote
 * rollout the target reports. A target that no longer needs it says so, and the issues are read again.
 */
async function reconcile(issue: DeploymentIssue): Promise<void> {
  const target = issue.deployment.targetId;
  if (!target || checkingTarget.value) return;
  checkingTarget.value = target;
  reconcileTarget.value = target;
  try {
    await nextTick();
    await reconcileState.refetch();
  } finally {
    checkingTarget.value = null;
  }
  const state = reconcileState.data.value;
  if (reconcileState.error.value || !state || state.targetId !== target) {
    notify({ tone: "danger", title: `Could not read ${target}`, description: "Its state is needed to reconcile it. Try again." });
    return;
  }
  if (state.condition === TargetCondition.NeedsReconciliation && state.active) {
    reconcileOpen.value = true;
    return;
  }
  notify({ tone: "info", title: `${target} no longer needs a reconcile`, description: "Its state changed since the last read." });
  void issues.refetch();
}

function promote(item: DeploymentPromotion): void {
  deploy.openDeploy({ target: item.prodTargetId, release: item.bundleId });
}

/** Opens the fleet dialog on the chosen releases, in the order the panel lists them. */
function promoteMany(items: DeploymentPromotion[]): void {
  if (!items.length) return;
  fleetItems.value = items;
  fleetOpen.value = true;
}

function fleetStarted(): void {
  notify({ tone: "success", title: "Fleet rollout started", description: "Running now follows it product by product." });
  void fleets.fleets.refetch();
}
</script>

<template>
  <div class="flex min-w-0 flex-col gap-6">
    <PageHeaderActions>
      <PortfolioFreezeToggle />
      <Button variant="solid" tone="primary" @click="deploy.openDeploy()">
        <template #leading><Rocket :size="16" /></template>Deploy
      </Button>
    </PageHeaderActions>
    <IssuesPanel
      v-if="shownIssues?.length || shownBuilds.length"
      :items="shownIssues ?? []"
      :builds="shownBuilds"
      :product-names="productNames"
      :checking-target="checkingTarget"
      @redeploy="redeploy"
      @reconcile="reconcile"
    />
    <RunningNowPanel
      v-if="running.data.value?.length || fleets.active.value"
      :items="running.data.value ?? []"
      :fleet="fleets.active.value"
      :cancelling-fleet="cancelFleet.loading.value"
      :product-names="productNames"
      @cancel-fleet="cancelFleet.mutateAsync($event)"
    />
    <ReadyToPromotePanel
      v-if="offers?.length || promotions.error.value"
      :items="offers"
      :product-names="productNames"
      :is-first-load="promotions.isFirstLoad.value"
      :error="promotions.error.value"
      :held-targets="heldTargets"
      @promote="promote"
      @promote-many="promoteMany"
      @retry="promotions.refetch()"
    />
    <FleetRolloutsPanel v-if="recentFleets.length" :items="recentFleets" :product-names="productNames" />
    <div class="flex min-w-0 flex-col gap-4">
      <DeploymentFilterScope :filter="route.filter.value" :tree="tree" @change="route.setFilter" />
      <RangeStatsTiles :filter="route.filter.value" />
      <DeploymentsPanel :has-filter-bar="false" />
    </div>
    <ReconcileModal
      v-if="reconcileTarget && reconcileState.data.value?.active"
      :target="reconcileTarget"
      :active="reconcileState.data.value.active"
      :open="reconcileOpen"
      @update:open="reconcileOpen = $event"
    />
    <FleetPromoteModal
      :open="fleetOpen"
      :items="fleetItems"
      :product-names="productNames"
      @update:open="fleetOpen = $event"
      @started="fleetStarted"
    />
    <DeployModal
      :key="deploy.session.value"
      :open="deploy.open.value"
      :selection="deploy.selection.value"
      @update:open="deploy.onOpenChange"
    />
  </div>
</template>
