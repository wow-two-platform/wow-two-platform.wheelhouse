<script setup lang="ts">
import { computed, nextTick, provide, ref, watch } from "vue";
import { RouterLink, useRoute, useRouter } from "vue-router";
import { LayoutGrid, Layers, List, Plus } from "lucide-vue-next";
import { Button } from "@wow-two-beta/fe/presentation/actions";
import { EmptyState } from "@wow-two-beta/fe/presentation/display";
import { DataRegion } from "@wow-two-beta/fe/presentation/feedback";
import { ToggleGroup, ToggleInput } from "@wow-two-beta/fe/presentation/forms";
import { PageHeaderActions } from "@wow-two-beta/fe/presentation/layout";
import { ConsolePaths, workspaceHash, workspaceOfHash } from "@/application/navigation";
import { usePins } from "@/application/pins";
import { TrendScalesKey, useProductFacts, usePortfolio, type PortfolioGroup } from "@/application/portfolio";
import { ProductsView, useGroupExpansion, useProductsView } from "@/application/preferences";
import { useRefresh } from "@/bootstrap/query";
import { PinKind } from "@/domain/pins";
import {
  ProductFilterQuery,
  ProductFilterRules,
  workspaceStatsOf,
  type ProductFilterState,
  type WorkspaceStats,
} from "@/domain/portfolio";
import type { Product } from "@/domain/products";
import type { Workspace } from "@/domain/workspaces";
import { RefreshButton } from "@/presentation/common/components";
import { useIsPhone } from "@/presentation/deployments/hooks/useIsPhone";
import ProductFilters from "../components/ProductFilters.vue";
import ProductFormModal from "../components/ProductFormModal.vue";
import ProductGroupSection from "../components/ProductGroupSection.vue";
import type { WorkspaceOption } from "../components/WorkspaceFilterList.vue";
import WorkspaceListRow from "../components/WorkspaceListRow.vue";
import WorkspacesModal from "../components/WorkspacesModal.vue";
import WorkspaceTile from "../components/WorkspaceTile.vue";

/**
 * The Products page: the workspaces first — each a card or row with its products' failing and needs-a-look counts,
 * its rollouts' success rate and weekly trend, a click scrolling to its products — then every workspace's products in
 * the owner's workspace order, as cards or rows. One Cards | Rows switch from tablets up governs both sections; phones
 * always show full-width cards. Search, workspace, status and sort live in the address (`q`, `workspace`, `status`,
 * `sort`); "Show inactive" is the console's one switch. A workspace's group is addressed by `#workspace-{slug}`, which
 * old `/w/{slug}` links and the scope bar's workspace crumb open.
 */
defineOptions({ name: "ProductsPage" });
const route = useRoute();
const router = useRouter();
const portfolio = usePortfolio();
const facts = useProductFacts();
const pins = usePins();
const viewChoice = useProductsView();
const expansion = useGroupExpansion();
const isPhone = useIsPhone();
const refresh = useRefresh(() => Promise.all([portfolio.products.reload(), portfolio.workspaces.reload(), facts.refresh()]));
const productForm = ref<{ open: boolean; product: Product | null }>({ open: false, product: null });
const managingWorkspaces = ref(false);
/* Every product trend on the page shares one scale, so a busy product reads taller than a quiet one. */
provide(TrendScalesKey, facts.scales);

/** Phones always show cards; the switch is not offered there. */
const view = computed(() => (isPhone.value ? ProductsView.Cards : viewChoice.value));
const firstLoad = computed(() => portfolio.isFirstLoad.value);
const filter = computed(() => ProductFilterQuery.parse(route.query));
const narrowed = computed(() => ProductFilterQuery.isNarrowed(filter.value));

/** A group's element id, which `#workspace-{slug}` names. @internal */
function groupIdOf(workspace: Workspace | null): string {
  return workspace ? workspaceHash(workspace.slug).slice(1) : "workspace-none";
}

/** Two placeholder groups during the first load, so the page keeps its shape. @internal */
const placeholders = [0, 1].map((index) => ({
  group: { workspace: null, products: [], total: 0, active: 0 } as PortfolioGroup,
  id: `placeholder-${index}`,
  stats: undefined,
  products: [null, null, null] as (Product | null)[],
}));

/** Each workspace group the workspace filter keeps, with its figures and its filtered, sorted products. @internal */
const sections = computed(() =>
  portfolio.groups.value
    .filter((group) => !filter.value.workspaces.length || (group.workspace && filter.value.workspaces.includes(group.workspace.slug)))
    .map((group) => {
      const stats: WorkspaceStats | undefined = facts.isLoading.value
        ? undefined
        : workspaceStatsOf(
            group.products.map((product) => facts.factsOf(product.slug)),
            group.workspace ? facts.workspaceSummaryOf(group.workspace.slug) : undefined,
          );
      return {
        group,
        id: groupIdOf(group.workspace),
        stats,
        products: ProductFilterRules.apply(group.products, facts.factsOf, filter.value),
      };
    }),
);
/** The groups to draw: placeholders on the first load; while narrowed, only groups with a match. @internal */
const shownSections = computed(() =>
  firstLoad.value
    ? placeholders
    : narrowed.value
      ? sections.value.filter((section) => section.products.length > 0)
      : sections.value,
);
const matchCount = computed(() => sections.value.reduce((sum, section) => sum + section.products.length, 0));
const workspaceOptions = computed<WorkspaceOption[]>(() =>
  portfolio.groups.value.flatMap((group) => {
    if (!group.workspace) return [];
    const failing = facts.isLoading.value
      ? false
      : group.products.some((product) => facts.factsOf(product.slug)?.condition === "failing");
    return [{ slug: group.workspace.slug, name: group.workspace.name, count: group.products.length, isFailing: failing }];
  }),
);
/** The workspace a new product starts in: the only one the filter chose. */
const chosenWorkspaceId = computed(() => {
  if (filter.value.workspaces.length !== 1) return null;
  return portfolio.workspaces.workspaces.value.find((item) => item.slug === filter.value.workspaces[0])?.id ?? null;
});

/** Whether a product is pinned; the pin shows on its card. @internal */
function isPinned(product: Product): boolean {
  return pins.isPinned({ kind: PinKind.Product, product });
}

/** Writes a filter change into the address; defaults leave it, and a workspace hash gives way. @internal */
function changeFilter(change: Partial<ProductFilterState>): void {
  const next = { ...filter.value, ...change };
  const rest = Object.fromEntries(
    Object.entries(route.query).filter(([key]) => !["q", "workspace", "status", "sort"].includes(key)),
  );
  void router.replace({ path: route.path, query: { ...rest, ...ProductFilterQuery.toQuery(next) } });
}

/** Unfolds a workspace's group and scrolls to it, keeping its hash in the address. @internal */
async function openWorkspace(workspace: Workspace | null): Promise<void> {
  const key = workspace?.id ?? "none";
  expansion.expand(key);
  if (workspace && route.hash !== workspaceHash(workspace.slug))
    await router.replace({ path: route.path, query: route.query, hash: workspaceHash(workspace.slug) });
  await nextTick();
  const reduced = globalThis.matchMedia?.("(prefers-reduced-motion: reduce)").matches ?? false;
  document.getElementById(groupIdOf(workspace))?.scrollIntoView({ behavior: reduced ? "auto" : "smooth", block: "start" });
}

/* A `#workspace-{slug}` address — an old /w/ link, the scope bar's crumb — scrolls to the group once it has drawn. */
watch(
  () => [route.hash, firstLoad.value] as const,
  ([hash, loading]) => {
    const slug = workspaceOfHash(hash);
    if (loading || !slug) return;
    const workspace = portfolio.workspaces.workspaces.value.find((item) => item.slug === slug) ?? null;
    if (workspace) void openWorkspace(workspace);
  },
  { immediate: true, flush: "post" },
);

/** Opens the product form: blank for a new product, or filled to edit one. @internal */
function openProductForm(product: Product | null): void {
  productForm.value = { open: true, product };
}

/** The empty line of a group the filter does not narrow. @internal */
function emptyTextOf(group: PortfolioGroup): string {
  return group.total > group.products.length
    ? "Only inactive products; turn on Show inactive to see them."
    : "No products in this workspace yet.";
}
</script>

<template>
  <div class="flex flex-col gap-6">
    <PageHeaderActions>
      <ToggleGroup
        v-if="!isPhone"
        variant="segmented"
        aria-label="View"
        :model-value="viewChoice"
        @update:model-value="(value: unknown) => value === ProductsView.Rows || value === ProductsView.Cards ? (viewChoice = value) : null"
      >
        <ToggleInput :value="ProductsView.Cards" size="sm"
          ><LayoutGrid :size="14" aria-hidden="true" /><span class="ml-1">Cards</span></ToggleInput
        >
        <ToggleInput :value="ProductsView.Rows" size="sm"
          ><List :size="14" aria-hidden="true" /><span class="ml-1">Rows</span></ToggleInput
        >
      </ToggleGroup>
      <RefreshButton size="md" icon-only :refreshing="refresh.refreshing.value" @refresh="refresh.refresh" />
      <Button
        variant="outline"
        tone="neutral"
        :shape="isPhone ? 'square' : 'default'"
        :aria-label="isPhone ? 'Workspaces' : undefined"
        title="Manage workspaces"
        @click="managingWorkspaces = true"
      >
        <template v-if="!isPhone" #leading><Layers :size="16" /></template
        ><Layers v-if="isPhone" :size="16" /><template v-else>Workspaces</template>
      </Button>
      <Button :is-disabled="productForm.open" @click="openProductForm(null)">
        <template #leading><Plus :size="16" /></template>New product
      </Button>
    </PageHeaderActions>

    <ProductFilters :filter="filter" :workspaces="workspaceOptions" @change="changeFilter" />

    <DataRegion
      :is-first-load="firstLoad"
      :error="portfolio.error.value"
      :has-data="portfolio.hasData.value"
      :is-empty="portfolio.hasData.value && !portfolio.products.products.value.length"
      empty-title="Your portfolio starts with a product"
      empty-description="Import the wow2 apps from the catalog, or add a product by hand."
      loading-label="Loading products"
      class="flex flex-col gap-8"
      @retry="refresh.refresh"
    >
      <template #emptyActions>
        <Button as-child><RouterLink :to="ConsolePaths.catalog">Import from the catalog</RouterLink></Button>
        <Button variant="outline" @click="openProductForm(null)">New product</Button>
      </template>

      <section aria-labelledby="workspaces-heading" class="flex min-w-0 flex-col gap-3">
        <h2 id="workspaces-heading" class="text-sm font-semibold text-muted-foreground">Workspaces</h2>
        <ul
          v-if="view === ProductsView.Rows"
          role="list"
          class="overflow-hidden rounded-2xl border border-border bg-card"
          aria-labelledby="workspaces-heading"
        >
          <WorkspaceListRow
            v-for="section in firstLoad ? placeholders : sections"
            :key="section.id"
            :workspace="section.group.workspace"
            :stats="section.stats"
            :weeks="facts.weeks.value"
            :is-loading="firstLoad"
            :group-id="section.id"
            @open="openWorkspace(section.group.workspace)"
          />
        </ul>
        <!-- Phones keep the products in reach: the workspace cards scroll sideways in one strip that bleeds to the edges. -->
        <ul
          v-else
          role="list"
          class="grid gap-3 max-sm:-mx-4 max-sm:flex max-sm:snap-x max-sm:snap-mandatory max-sm:scroll-px-4 max-sm:overflow-x-auto max-sm:px-4 max-sm:pb-1 max-sm:[scrollbar-width:none] sm:grid-cols-2 lg:grid-cols-3 2xl:grid-cols-4"
          aria-labelledby="workspaces-heading"
        >
          <li
            v-for="section in firstLoad ? placeholders : sections"
            :key="section.id"
            class="flex min-w-0 max-sm:w-[82%] max-sm:shrink-0 max-sm:snap-start"
          >
            <WorkspaceTile
              class="flex-1"
              :workspace="section.group.workspace"
              :stats="section.stats"
              :weeks="facts.weeks.value"
              :is-loading="firstLoad"
              :group-id="section.id"
              @open="openWorkspace(section.group.workspace)"
            />
          </li>
        </ul>
      </section>

      <section aria-labelledby="products-heading" class="flex min-w-0 flex-col gap-3">
        <h2 id="products-heading" class="text-sm font-semibold text-muted-foreground">
          Products<span v-if="narrowed && !firstLoad" class="font-normal"> · {{ matchCount }} match</span>
        </h2>
        <EmptyState
          v-if="narrowed && !firstLoad && !matchCount"
          title="No product matches"
          description="Try another search, status or workspace."
        >
          <template #actions>
            <Button variant="outline" @click="changeFilter({ search: '', workspaces: [], status: 'all' })"
              >Clear filters</Button
            >
          </template>
        </EmptyState>
        <div v-else class="flex flex-col gap-8">
          <ProductGroupSection
            v-for="section in shownSections"
            :key="section.id"
            :workspace="section.group.workspace"
            :products="section.products"
            :group-id="section.id"
            :view="view"
            :facts-of="facts.factsOf"
            :weeks="facts.weeks.value"
            :is-pinned="isPinned"
            :is-expanded="firstLoad || narrowed || expansion.isExpanded(section.group.workspace?.id ?? 'none')"
            :empty-text="emptyTextOf(section.group)"
            :is-loading="firstLoad"
            @toggle="expansion.toggle(section.group.workspace?.id ?? 'none')"
            @edit="openProductForm"
          />
        </div>
      </section>
    </DataRegion>

    <ProductFormModal
      :open="productForm.open"
      :product="productForm.product"
      :operations="portfolio.products"
      :workspaces="portfolio.workspaces.workspaces.value"
      :default-workspace-id="chosenWorkspaceId"
      @update:open="(open) => (productForm = { ...productForm, open })"
      @saved="(slug) => !productForm.product && router.push(ConsolePaths.product(slug))"
    />
    <WorkspacesModal v-model:open="managingWorkspaces" />
  </div>
</template>
