<script lang="ts">
/** Defines the deployments panel: the whole portfolio, or one product's deployments. */
export interface DeploymentsPanelProps {
  /** Locks the entity filter to this product, as a product's Deployments tab does. */
  readonly lockedProduct?: string | null;
  /** Holds the filter bar inside the panel; off where the page sets the filter above it, as the Deployments page does. */
  readonly hasFilterBar?: boolean;
}
</script>

<script setup lang="ts">
import { computed, ref, useTemplateRef } from "vue";
import { CalendarDays, List, MousePointerClick, X } from "lucide-vue-next";

import { Button } from "@wow-two-beta/fe/presentation/actions";
import { ToggleGroup, ToggleInput } from "@wow-two-beta/fe/presentation/forms";
import { DataRegion } from "@wow-two-beta/fe/presentation/feedback";
import { TailwindBreakpoints, useBreakpoint } from "@wow-two-beta/fe/foundation/device";
import { useResizeObserver } from "@wow-two-beta/fe/foundation/observers";

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
import DeploymentDayStats from "./DeploymentDayStats.vue";
import DeploymentFilterBar from "./DeploymentFilterBar.vue";
import DeploymentList from "./DeploymentList.vue";
import DeploymentsInfo from "./DeploymentsInfo.vue";
import RangeStatsTiles from "./RangeStatsTiles.vue";

/** The day panel's narrowest width beside the calendar, with the gap before it: 18rem + 1.5rem. @internal */
const SidePanelRoom = 312;
/** The most deployments one read of a day takes (the API's page limit), so its counts are whole on any real day. @internal */
const DayPageSize = 100;

/**
 * Deployments under one shared filter, shown two ways: a calendar of outcomes per day (Grid), whose day expands into
 * that day's deployments, or every deployment newest first (List). From `lg`, where the panel has room beside the
 * calendar, a day panel sits to its right: the chosen day's counts and deployments, or one line asking for a day;
 * narrower, the chosen day stacks under the calendar. The day's counts are tiles for each status that occurred, read
 * from its deployments, whose rows carry the same status pills. The range's totals are the measures above, never
 * repeated here; where the panel holds its own filter bar (a product's tab), the measures sit under it. The calendar's
 * key and the statuses are behind the info button beside the title. The filter, the view and the expanded day live in
 * the address.
 */
defineOptions({ name: "DeploymentsPanel" });
const props = withDefaults(defineProps<DeploymentsPanelProps>(), { lockedProduct: null, hasFilterBar: true });
const route = useDeploymentViewRoute(() => props.lockedProduct);
const portfolio = usePortfolio();
const tree = computed(() => buildEntityTree(portfolio.groups.value, portfolio.showInactive.value));
const productNames = computed(
  () => new Map(portfolio.products.products.value.map((product) => [product.slug, product.name] as const)),
);
const isGrid = computed(() => route.view.value === DeploymentView.Grid);

const activity = useDeploymentActivity(route.filter, isGrid);
const list = useDeploymentPages(route.filter, { enabled: () => !isGrid.value });
const dayList = useDeploymentPages(route.filter, {
  day: route.day,
  limit: DayPageSize,
  enabled: () => isGrid.value && route.day.value !== null,
});
const running = useRunningDeployments(() => route.filter.value.entities);
const isLive = computed(() => (running.data.value?.length ?? 0) > 0);
const refresh = useRefresh(() =>
  Promise.all(isGrid.value ? [activity.refetch(), ...(route.day.value ? [dayList.refetch()] : [])] : [list.refetch()]),
);

const breakpoint = useBreakpoint(TailwindBreakpoints);
const gridArea = useTemplateRef<HTMLElement>("gridArea");
const gridBox = useTemplateRef<HTMLElement>("gridBox");
/** The room left beside the calendar, which sizes to its days. */
const spare = ref(0);
/** Measures both: the panel can widen, and the calendar widens with a longer range. @internal */
function measure(): void {
  const area = gridArea.value;
  spare.value = area ? area.clientWidth - (gridBox.value?.offsetWidth ?? area.clientWidth) : 0;
}
useResizeObserver(gridArea, measure);
useResizeObserver(gridBox, measure);
/** The day panel sits beside the calendar from `lg`, when the panel holds both. */
const isSide = computed(
  () => (breakpoint.value === "lg" || breakpoint.value === "xl" || breakpoint.value === "2xl") && spare.value >= SidePanelRoom,
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
  <Panel title="Deployments">
    <template #info>
      <DeploymentsInfo :status="route.filter.value.status" :is-grid="isGrid" />
    </template>
    <template #actions>
      <ToggleGroup variant="segmented" aria-label="View" :model-value="route.view.value" @update:model-value="setView">
        <ToggleInput :value="DeploymentView.Grid" size="sm" class="max-md:min-h-9">
          <CalendarDays :size="14" aria-hidden="true" /><span class="ml-1">Grid</span>
        </ToggleInput>
        <ToggleInput :value="DeploymentView.List" size="sm" class="max-md:min-h-9">
          <List :size="14" aria-hidden="true" /><span class="ml-1">List</span>
        </ToggleInput>
      </ToggleGroup>
      <RefreshButton variant="ghost" size="md" icon-only class="max-md:size-11" :refreshing="refresh.refreshing.value" @refresh="refresh.refresh()" />
    </template>
    <div class="flex min-w-0 flex-col gap-5">
      <DeploymentFilterBar
        v-if="props.hasFilterBar"
        :filter="route.filter.value"
        :tree="tree"
        :is-entity-locked="Boolean(props.lockedProduct)"
        @change="route.setFilter"
      />
      <RangeStatsTiles v-if="props.hasFilterBar" :filter="route.filter.value" is-inset />
      <div
        v-if="isGrid"
        ref="gridArea"
        :class="['flex min-w-0 gap-5', isSide ? 'flex-row items-start gap-6' : 'flex-col']"
      >
        <div ref="gridBox" class="w-max max-w-full min-w-0 flex-none">
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
        </div>
        <section
          v-if="route.day.value"
          :class="[
            'flex min-w-0 flex-col gap-3',
            isSide ? 'flex-1 self-stretch border-l border-border pl-6' : 'border-t border-border pt-4',
          ]"
          aria-live="polite"
        >
          <div class="flex items-center justify-between gap-3">
            <h3 class="text-sm font-semibold">{{ dayTitle }}</h3>
            <Button variant="ghost" tone="neutral" size="sm" :aria-label="`Close ${dayTitle}`" class="max-md:min-h-11" @click="route.setDay(null)">
              <template #leading><X :size="14" aria-hidden="true" /></template>Close
            </Button>
          </div>
          <DeploymentDayStats
            :items="dayList.loading.value ? undefined : dayList.items.value"
            :has-more="dayList.hasNextPage.value"
          />
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
              :layout="isSide ? 'rows' : 'table'"
              @more="dayList.fetchNextPage()"
            />
          </DataRegion>
        </section>
        <p
          v-else-if="isSide"
          class="flex flex-1 items-center justify-center gap-2 self-stretch border-l border-border pl-6 text-sm text-muted-foreground"
        >
          <MousePointerClick :size="16" aria-hidden="true" />Choose a day to see its deployments.
        </p>
      </div>
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
