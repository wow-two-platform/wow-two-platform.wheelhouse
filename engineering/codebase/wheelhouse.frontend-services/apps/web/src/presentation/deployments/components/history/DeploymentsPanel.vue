<script lang="ts">
/** Defines the deployments panel: the whole portfolio, or one product's deployments. */
export interface DeploymentsPanelProps {
  /** Locks the entity filter to this product, as a product's Deployments tab does. */
  readonly lockedProduct?: string | null;
}
</script>

<script setup lang="ts">
import { computed } from "vue";
import { CalendarDays, List, X } from "lucide-vue-next";

import { Button } from "@wow-two-beta/fe/presentation/actions";
import { ToggleGroup, ToggleInput } from "@wow-two-beta/fe/presentation/forms";
import { DataRegion } from "@wow-two-beta/fe/presentation/feedback";

import {
  buildEntityTree,
  DeploymentView,
  useDeploymentActivity,
  useDeploymentPages,
  useRunningDeployments,
} from "@/application/deployments";
import { usePortfolio } from "@/application/portfolio";
import { useRefresh } from "@/bootstrap/query";
import { Panel, RefreshButton } from "@/presentation/common/components";

import { useDeploymentViewRoute } from "../../hooks/useDeploymentViewRoute";
import ActivityGrid from "./ActivityGrid.vue";
import DeploymentFilterBar from "./DeploymentFilterBar.vue";
import DeploymentList from "./DeploymentList.vue";

/**
 * Deployments under one shared filter, shown two ways: a calendar of outcomes per day (Grid), whose day expands into
 * that day's deployments, or every deployment newest first (List). The filter, the view and the expanded day live in
 * the address.
 */
defineOptions({ name: "DeploymentsPanel" });
const props = withDefaults(defineProps<DeploymentsPanelProps>(), { lockedProduct: null });
const route = useDeploymentViewRoute(() => props.lockedProduct);
const portfolio = usePortfolio();
const tree = computed(() => buildEntityTree(portfolio.groups.value, portfolio.showInactive.value));
const productNames = computed(
  () => new Map(portfolio.products.products.value.map((product) => [product.slug, product.name] as const)),
);
const isGrid = computed(() => route.view.value === DeploymentView.Grid);

const activity = useDeploymentActivity(route.filter, isGrid);
const list = useDeploymentPages(route.filter, { enabled: () => !isGrid.value });
const dayList = useDeploymentPages(route.filter, { day: route.day, enabled: () => isGrid.value && route.day.value !== null });
const running = useRunningDeployments(() => route.filter.value.entities);
const isLive = computed(() => (running.data.value?.length ?? 0) > 0);
const refresh = useRefresh(() =>
  Promise.all(isGrid.value ? [activity.refetch(), ...(route.day.value ? [dayList.refetch()] : [])] : [list.refetch()]),
);
const dayTitle = computed(() => {
  const day = route.day.value;
  if (!day) return "";
  const [year, month, date] = day.split("-").map(Number) as [number, number, number];
  return new Intl.DateTimeFormat(undefined, { weekday: "long", month: "long", day: "numeric", year: "numeric" }).format(
    new Date(year, month - 1, date),
  );
});

/** Switches Grid and List; pressing the active segment keeps the view. @internal */
function setView(value: unknown): void {
  if (value === DeploymentView.Grid || value === DeploymentView.List) route.setView(value);
}
</script>

<template>
  <Panel
    title="Deployments"
    :description="
      props.lockedProduct
        ? 'This product\'s rollouts by day, or every one newest first.'
        : 'Rollouts by day, or every one newest first, under one filter.'
    "
  >
    <template #actions>
      <ToggleGroup variant="segmented" aria-label="View" :model-value="route.view.value" @update:model-value="setView">
        <ToggleInput :value="DeploymentView.Grid" size="sm" class="min-h-11 md:min-h-0">
          <CalendarDays :size="14" aria-hidden="true" /><span class="ml-1">Grid</span>
        </ToggleInput>
        <ToggleInput :value="DeploymentView.List" size="sm" class="min-h-11 md:min-h-0">
          <List :size="14" aria-hidden="true" /><span class="ml-1">List</span>
        </ToggleInput>
      </ToggleGroup>
      <RefreshButton variant="ghost" icon-only :refreshing="refresh.refreshing.value" @refresh="refresh.refresh()" />
    </template>
    <div class="flex min-w-0 flex-col gap-5">
      <DeploymentFilterBar
        :filter="route.filter.value"
        :tree="tree"
        :is-entity-locked="Boolean(props.lockedProduct)"
        @change="route.setFilter"
      />
      <template v-if="isGrid">
        <DataRegion
          :is-first-load="activity.isFirstLoad.value"
          :is-refreshing="activity.isRefreshing.value"
          :error="activity.error.value"
          :has-data="Boolean(activity.data.value)"
          loading-label="Loading deployment activity"
          @retry="activity.refetch()"
        >
          <ActivityGrid
            :activity="activity.data.value"
            :status="route.filter.value.status"
            :selected="route.day.value"
            :is-live="isLive"
            @select="route.setDay"
          />
        </DataRegion>
        <section v-if="route.day.value" class="flex min-w-0 flex-col gap-3 border-t border-border pt-4" aria-live="polite">
          <div class="flex items-center justify-between gap-3">
            <h3 class="text-sm font-semibold">{{ dayTitle }}</h3>
            <Button variant="ghost" tone="neutral" size="sm" :aria-label="`Close ${dayTitle}`" class="min-h-11 md:min-h-9" @click="route.setDay(null)">
              <template #leading><X :size="14" aria-hidden="true" /></template>Close
            </Button>
          </div>
          <DataRegion
            :is-first-load="dayList.loading.value"
            :error="dayList.error.value"
            :has-data="!dayList.loading.value"
            :is-empty="!dayList.loading.value && !dayList.items.value.length"
            empty-title="No deployments that day"
            empty-description="Nothing under this filter was submitted on this day."
            loading-label="Loading the day's deployments"
          >
            <DeploymentList
              :items="dayList.loading.value ? undefined : dayList.items.value"
              :product-names="productNames"
              :has-more="dayList.hasNextPage.value"
              :is-loading-more="dayList.isFetchingNextPage.value"
              :is-product-hidden="Boolean(props.lockedProduct)"
              @more="dayList.fetchNextPage()"
            />
          </DataRegion>
        </section>
      </template>
      <DataRegion
        v-else
        :is-first-load="list.loading.value"
        :error="list.error.value"
        :has-data="!list.loading.value"
        :is-empty="!list.loading.value && !list.items.value.length"
        empty-title="No deployments match"
        empty-description="Widen the range, choose all outcomes, or clear the products and services."
        loading-label="Loading deployments"
      >
        <DeploymentList
          :items="list.loading.value ? undefined : list.items.value"
          :product-names="productNames"
          :has-more="list.hasNextPage.value"
          :is-loading-more="list.isFetchingNextPage.value"
          :is-product-hidden="Boolean(props.lockedProduct)"
          @more="list.fetchNextPage()"
        />
      </DataRegion>
    </div>
  </Panel>
</template>
