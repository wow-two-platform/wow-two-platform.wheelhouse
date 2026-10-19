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
import { computed, nextTick, ref, useTemplateRef, watch } from "vue";
import { useRoute, useRouter } from "vue-router";
import { CalendarDays, List, MousePointerClick, X } from "lucide-vue-next";

import { Button } from "@wow-two-beta/fe/presentation/actions";
import { Badge } from "@wow-two-beta/fe/presentation/display";
import { ToggleGroup, ToggleInput } from "@wow-two-beta/fe/presentation/forms";
import { DataRegion } from "@wow-two-beta/fe/presentation/feedback";
import { TailwindBreakpoints, useBreakpoint } from "@wow-two-beta/fe/foundation/device";
import { useResizeObserver } from "@wow-two-beta/fe/foundation/observers";

import {
  buildEntityTree,
  DeploymentView,
  isSelectedRelease,
  readReleaseSelection,
  ReleaseQueryKeys,
  useDeploymentActivity,
  useDeploymentPages,
  useDeploymentTargets,
  useRunningDeployments,
} from "@/application/deployments";
import { usePortfolio } from "@/application/portfolio";
import { useShowAllAttempts } from "@/application/preferences";
import { DayLanes, DeploymentFilters } from "@/domain/deployments";
import { useRefresh } from "@/bootstrap/query";
import { InfoPopover, Panel, RefreshButton } from "@/presentation/common/components";

import { useDeploymentViewRoute } from "../../hooks/useDeploymentViewRoute";
import ActivityGrid from "./ActivityGrid.vue";
import DayProductRows from "./DayProductRows.vue";
import DeploymentDayStats from "./DeploymentDayStats.vue";
import DeploymentFilterBar from "./DeploymentFilterBar.vue";
import DeploymentList from "./DeploymentList.vue";
import DeploymentStatusLegend from "./DeploymentStatusLegend.vue";
import DeploymentsInfo from "./DeploymentsInfo.vue";
import RangeStatsTiles from "./RangeStatsTiles.vue";

/** The day panel's narrowest width beside the calendar, with the gap before it: 18rem + 1.5rem. @internal */
const SidePanelRoom = 312;
/** The most deployments one read of a day takes (the API's page limit), so its counts are whole on any real day. @internal */
const DayPageSize = 100;
/** The most older pages the List reads on its own while looking for a chosen release. @internal */
const SelectionPageLimit = 20;

/**
 * Deployments under one shared filter, shown two ways: a calendar of outcomes per day (Grid), whose day expands into
 * that day's deployments, or the deployments newest first (List). Both show each target's latest deploy of a day until
 * the viewer's All attempts toggle in the filter bar shows every attempt, when the calendar also marks mixed days. From `lg`, where the panel has room beside the
 * calendar, a day panel sits to its right: the chosen day's date and counts over one compact row per product, a lane
 * per environment with a glyph per deploy (`DayProductRows`), or one line asking for a day; narrower, the chosen day
 * stacks under the calendar. A glyph opens its release on the product's Deployments tab, whose List chooses the
 * address's `release` (and `env`) rows and scrolls to them. The range's totals are the measures above, never repeated
 * here; where the panel holds its own filter bar (a product's tab), the measures sit under it. The calendar's key and
 * the statuses are behind the info button beside the title. The filter, the view and the expanded day live in the
 * address.
 */
defineOptions({ name: "DeploymentsPanel" });
const props = withDefaults(defineProps<DeploymentsPanelProps>(), { lockedProduct: null, hasFilterBar: true });
const route = useDeploymentViewRoute(() => props.lockedProduct);
const address = useRoute();
const router = useRouter();
const portfolio = usePortfolio();
const targets = useDeploymentTargets();
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
/** The viewer's All attempts toggle, in the filter bar: while on, the calendar marks mixed days. */
const showAllAttempts = useShowAllAttempts();
const isLive = computed(() => (running.data.value?.length ?? 0) > 0);

/** The chosen day's rows, one per product, and the environments their lanes cover. */
const dayItems = computed(() => (dayList.loading.value ? undefined : dayList.items.value));
const dayRows = computed(() =>
  dayItems.value ? DayLanes.rows(dayItems.value, productNames.value, targets.data.value ?? []) : undefined,
);
const dayEnvironments = computed(() => DayLanes.environments(dayItems.value ?? []));
const isToday = computed(() => route.day.value === DeploymentFilters.localDate(new Date()));

/** The release the address chose (a glyph's link): the List marks its rows, reads older pages to find them, and
 * scrolls to the first. */
const selection = computed(() => readReleaseSelection(address.query));
const selectedIds = computed(
  () => new Set(list.items.value.filter((item) => isSelectedRelease(item, selection.value)).map((item) => item.id)),
);
const selectionLabel = computed(() =>
  selection.value ? [selection.value.release, selection.value.environment].filter(Boolean).join(" on ") : "",
);
const isSelectionMissing = computed(
  () =>
    Boolean(selection.value) &&
    !isGrid.value &&
    !list.loading.value &&
    !selectedIds.value.size &&
    !list.hasNextPage.value &&
    !list.isFetchingNextPage.value,
);
let pagesRead = 0;
let scrolledTo: string | null = null;
watch(
  () => [selection.value?.release, selection.value?.environment, isGrid.value] as const,
  () => {
    pagesRead = 0;
    scrolledTo = null;
  },
);
watch(
  () =>
    [selectedIds.value.size, list.hasNextPage.value, list.isFetchingNextPage.value, list.loading.value, isGrid.value] as const,
  async ([found, hasMore, isFetching, isLoading, grid]) => {
    const chosen = selection.value;
    if (!chosen || grid || isLoading) return;
    if (!found) {
      if (hasMore && !isFetching && pagesRead < SelectionPageLimit) {
        pagesRead += 1;
        void list.fetchNextPage();
      }
      return;
    }
    const key = `${chosen.release}|${chosen.environment ?? ""}`;
    if (scrolledTo === key) return;
    scrolledTo = key;
    await nextTick();
    document.querySelector<HTMLElement>("[data-selected-release]")?.scrollIntoView({ block: "center" });
  },
  { immediate: true, flush: "post" },
);

/** Clears the chosen release from the address; the environment stays, as the product page reads it too. @internal */
function clearSelection(): void {
  const query = Object.fromEntries(Object.entries(address.query).filter(([key]) => key !== ReleaseQueryKeys[0]));
  void router.replace({ path: address.path, query, hash: address.hash });
}
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

/** The chosen day's date, its year left out in the current one so the today tag fits beside it. */
const dayTitle = computed(() => {
  const day = route.day.value;
  if (!day) return "";
  const [year, month, date] = day.split("-").map(Number) as [number, number, number];
  const isThisYear = year === new Date().getFullYear();
  return new Intl.DateTimeFormat(undefined, {
    weekday: "short",
    month: "long",
    day: "numeric",
    ...(isThisYear ? {} : { year: "numeric" }),
  }).format(new Date(year, month - 1, date));
});

/** Switches Grid and List; pressing the active segment keeps the view. @internal */
function setView(value: unknown): void {
  if (value === DeploymentView.Grid || value === DeploymentView.List) route.setView(value);
}
</script>

<template>
  <Panel title="Deployments">
    <template #info>
      <DeploymentsInfo :is-grid="isGrid" />
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
              :selected="route.day.value"
              :is-live="isLive"
              :is-audit="showAllAttempts"
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
          data-info-scope
        >
          <div class="flex flex-col gap-1.5">
            <div class="flex items-center justify-between gap-3">
              <div class="flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1">
                <h3 class="text-sm font-semibold">{{ dayTitle }}</h3>
                <Badge v-if="isToday" variant="neutral">today</Badge>
                <InfoPopover title="The day">
                  <p class="mb-2.5">
                    One row per product. Each environment's lane holds its last deploy that day, what it runs now; with
                    All attempts on, a glyph per attempt, oldest first, those that changed nothing dashed. A bar on
                    the left marks a row whose latest deploy needs action (red) or a look (amber); those rows lead. Point
                    at a glyph for its release, time, state and actor, or tap it on touch; choose it to open the release
                    on the product's Deployments tab. The counts above say how many of the deploys shown reached each
                    state.
                  </p>
                  <DeploymentStatusLegend />
                </InfoPopover>
              </div>
              <!-- Icon-only on a phone, so the date, its today tag and the info button keep one line. -->
              <Button
                variant="ghost"
                tone="neutral"
                size="sm"
                :aria-label="`Close ${dayTitle}`"
                class="shrink-0 max-md:size-11 max-md:px-0"
                @click="route.setDay(null)"
              >
                <template #leading><X :size="14" aria-hidden="true" /></template><span class="max-md:hidden">Close</span>
              </Button>
            </div>
            <DeploymentDayStats :items="dayItems" :has-more="dayList.hasNextPage.value" />
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
            <DayProductRows :rows="dayRows" :environments="dayEnvironments" :range="route.filter.value.range" />
            <div v-if="dayList.hasNextPage.value" class="mt-3 flex justify-center">
              <Button
                variant="outline"
                tone="neutral"
                size="sm"
                :is-loading="dayList.isFetchingNextPage.value"
                class="max-md:min-h-11"
                @click="dayList.fetchNextPage()"
                >Load older deployments</Button
              >
            </div>
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
        empty-description="Widen the range, show all attempts, or clear the products and services."
        loading-label="Loading deployments"
      >
        <div v-if="selection" class="mb-3 flex min-w-0 flex-wrap items-center gap-2 text-sm">
          <span class="inline-flex min-w-0 items-center gap-1.5 rounded-full border border-primary/40 bg-primary/10 py-0.5 ps-2.5 pe-1">
            <span class="truncate"
              >Release <span class="font-mono text-xs font-medium">{{ selectionLabel }}</span></span
            >
            <Button
              variant="ghost"
              tone="neutral"
              size="sm"
              shape="square"
              class="size-6 max-md:size-9"
              :aria-label="`Clear the chosen release ${selectionLabel}`"
              @click="clearSelection"
              ><template #leading><X :size="12" aria-hidden="true" /></template
            ></Button>
          </span>
          <span v-if="isSelectionMissing" class="text-muted-foreground">Not deployed in this range.</span>
        </div>
        <DeploymentList
          :items="list.loading.value ? undefined : list.items.value"
          :product-names="productNames"
          :has-more="list.hasNextPage.value"
          :is-loading-more="list.isFetchingNextPage.value"
          :is-product-hidden="Boolean(props.lockedProduct)"
          :selected-ids="selectedIds"
          @more="list.fetchNextPage()"
        />
      </DataRegion>
    </div>
  </Panel>
</template>
