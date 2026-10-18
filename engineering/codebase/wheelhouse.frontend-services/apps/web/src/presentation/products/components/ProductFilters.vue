<script lang="ts">
import type { ProductFilterState } from "@/domain/portfolio";
import type { WorkspaceOption } from "./WorkspaceFilterList.vue";

/** The Products page's search, filter and sort. */
export interface ProductFiltersProps {
  readonly filter: ProductFilterState;
  readonly workspaces: readonly WorkspaceOption[];
}
</script>
<script setup lang="ts">
import { computed, ref } from "vue";
import { ChevronDown, Layers, SlidersHorizontal } from "lucide-vue-next";
import { Button } from "@wow-two-beta/fe/presentation/actions";
import { CountBadge } from "@wow-two-beta/fe/presentation/display";
import {
  SearchInput,
  SelectPicker,
  SelectPickerContent,
  SelectPickerItem,
  SelectPickerTrigger,
  SelectPickerValue,
} from "@wow-two-beta/fe/presentation/forms";
import { BottomSheet, BottomSheetTitle, Popover, PopoverContent, PopoverTrigger } from "@wow-two-beta/fe/presentation/overlays";
import {
  ProductFilterQuery,
  ProductSort,
  ProductSortLabels,
  StatusFilter,
  StatusFilterLabels,
  type ProductFilterState as Filter,
} from "@/domain/portfolio";
import { useIsPhone } from "@/presentation/deployments/hooks/useIsPhone";
import ShowInactiveSwitch from "@/presentation/shell/components/ShowInactiveSwitch.vue";
import ChoiceChips from "./ChoiceChips.vue";
import WorkspaceFilterList from "./WorkspaceFilterList.vue";

/**
 * One row that governs the workspaces and products below it: search by name, slug or description; workspaces (a red
 * mark on one whose products are failing); status; the one "Show inactive" switch; and the sort, name by default.
 * The page keeps search, filter and sort in its address. On a phone the row holds the search and one "Filter" button
 * whose sheet carries the rest, sorting included.
 */
defineOptions({ name: "ProductFilters" });
const props = defineProps<ProductFiltersProps>();
const emit = defineEmits<{ change: [filter: Partial<Filter>] }>();
defineSlots<{}>();
const isPhone = useIsPhone();
const sheetOpen = ref(false);
const workspacesOpen = ref(false);

const workspaceSummary = computed(() => {
  const chosen = props.workspaces.filter((option) => props.filter.workspaces.includes(option.slug));
  if (!chosen.length) return "All workspaces";
  return chosen.length === 1 ? chosen[0]!.name : `${chosen.length} workspaces`;
});
const anyFailing = computed(() => props.workspaces.some((option) => option.isFailing));
/** How many filters narrow the view besides the search, for the phone's button. @internal */
const narrowing = computed(() => props.filter.workspaces.length + (props.filter.status === StatusFilter.All ? 0 : 1));
const sortChanged = computed(() => props.filter.sort !== ProductSort.Name);

/** Narrows by status; the picker's clear returns to any status. @internal */
function setStatus(value: unknown): void {
  emit("change", { status: (Object.values(StatusFilter) as unknown[]).includes(value) ? (value as StatusFilter) : StatusFilter.All });
}

function setSort(value: unknown): void {
  emit("change", { sort: (Object.values(ProductSort) as unknown[]).includes(value) ? (value as ProductSort) : ProductSort.Name });
}

function clear(): void {
  emit("change", { search: "", workspaces: [], status: StatusFilter.All });
}
</script>
<template>
  <div class="flex min-w-0 flex-wrap items-center gap-2" role="search" aria-label="Find products">
    <SearchInput
      :model-value="props.filter.search"
      size="md"
      class="min-w-0 max-md:flex-1 md:w-64"
      placeholder="Search products"
      aria-label="Search products by name, slug or description"
      @update:model-value="emit('change', { search: $event })"
    />

    <template v-if="isPhone">
      <Button
        variant="outline"
        tone="neutral"
        size="md"
        class="min-h-11 shrink-0"
        aria-haspopup="dialog"
        :aria-label="`Filter and sort${narrowing ? `: ${narrowing} filters` : ''}`"
        @click="sheetOpen = true"
      >
        <template #leading><SlidersHorizontal :size="16" aria-hidden="true" /></template>Filter
        <CountBadge v-if="narrowing" :value="narrowing" variant="neutral" class="ms-1" />
        <span v-else-if="anyFailing" class="ms-0.5 size-2 rounded-full bg-destructive" aria-hidden="true" />
      </Button>
      <BottomSheet v-model:open="sheetOpen" :snap-points="['80vh', '94vh']">
        <BottomSheetTitle class="px-1 pb-2 text-base font-semibold">Filter and sort</BottomSheetTitle>
        <div class="flex flex-col gap-5 px-1 pb-2">
          <section>
            <h3 class="mb-2 text-xs font-semibold text-muted-foreground">Sort</h3>
            <ChoiceChips label="Sort" :options="ProductSortLabels" :model-value="props.filter.sort" @update:model-value="setSort" />
          </section>
          <section>
            <h3 class="mb-2 text-xs font-semibold text-muted-foreground">Status</h3>
            <ChoiceChips label="Status" :options="StatusFilterLabels" :model-value="props.filter.status" @update:model-value="setStatus" />
          </section>
          <section aria-labelledby="sheet-workspaces">
            <h3 id="sheet-workspaces" class="mb-1.5 text-xs font-semibold text-muted-foreground">Workspaces</h3>
            <WorkspaceFilterList
              :options="props.workspaces"
              :model-value="props.filter.workspaces"
              @update:model-value="emit('change', { workspaces: $event })"
            />
          </section>
          <ShowInactiveSwitch />
        </div>
        <div class="flex gap-2 px-1 pt-2">
          <Button v-if="ProductFilterQuery.isNarrowed(props.filter)" variant="outline" tone="neutral" class="min-h-11 flex-1" @click="clear"
            >Clear filters</Button
          >
          <Button class="min-h-11 flex-1" @click="sheetOpen = false">Done</Button>
        </div>
      </BottomSheet>
    </template>

    <template v-else>
      <Popover v-model:open="workspacesOpen" placement="bottom-start">
        <PopoverTrigger as-child>
          <Button variant="outline" tone="neutral" size="md" class="max-w-full" :aria-label="`Workspaces: ${workspaceSummary}`">
            <template #leading><Layers :size="16" aria-hidden="true" /></template>
            <span class="truncate">{{ workspaceSummary }}</span>
            <span
              v-if="anyFailing && !props.filter.workspaces.length"
              class="size-2 shrink-0 rounded-full bg-destructive"
              title="A workspace has failing products"
              aria-hidden="true"
            />
            <ChevronDown :size="14" class="shrink-0 text-muted-foreground" aria-hidden="true" />
          </Button>
        </PopoverTrigger>
        <PopoverContent class="w-[min(18rem,calc(100vw-2rem))] p-1.5" aria-label="Choose workspaces">
          <WorkspaceFilterList
            :options="props.workspaces"
            :model-value="props.filter.workspaces"
            @update:model-value="emit('change', { workspaces: $event })"
          />
          <Button
            v-if="props.filter.workspaces.length"
            size="sm"
            variant="ghost"
            tone="neutral"
            class="mt-1 w-full"
            @click="emit('change', { workspaces: [] })"
            >All workspaces</Button
          >
        </PopoverContent>
      </Popover>
      <SelectPicker
        :model-value="props.filter.status === StatusFilter.All ? null : props.filter.status"
        is-clearable
        clear-label="Any status"
        :get-option-label="(value) => StatusFilterLabels[value as StatusFilter]"
        @update:model-value="setStatus"
        ><SelectPickerTrigger aria-label="Status" width="10.5rem"><SelectPickerValue placeholder="Any status" /></SelectPickerTrigger
        ><SelectPickerContent
          ><SelectPickerItem
            v-for="value in [StatusFilter.Failing, StatusFilter.Attention, StatusFilter.Healthy, StatusFilter.Idle]"
            :key="value"
            :item-key="value"
            :label="StatusFilterLabels[value]" /></SelectPickerContent
      ></SelectPicker>
      <SelectPicker
        :model-value="props.filter.sort"
        :get-option-label="(value) => `Sort: ${ProductSortLabels[value as ProductSort]}`"
        @update:model-value="setSort"
        ><SelectPickerTrigger aria-label="Sort" width="12.5rem" :class="sortChanged ? 'border-primary/40' : ''"
          ><SelectPickerValue /></SelectPickerTrigger
        ><SelectPickerContent
          ><SelectPickerItem
            v-for="(label, value) in ProductSortLabels"
            :key="value"
            :item-key="value"
            :label="`Sort: ${label}`"
            :text="label" /></SelectPickerContent
      ></SelectPicker>
      <ShowInactiveSwitch class="ms-1" />
      <Button
        v-if="ProductFilterQuery.isNarrowed(props.filter)"
        variant="ghost"
        tone="neutral"
        size="md"
        @click="clear"
        >Clear</Button
      >
    </template>
  </div>
</template>
