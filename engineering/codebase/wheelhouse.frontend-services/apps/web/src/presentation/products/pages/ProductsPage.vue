<script setup lang="ts">
import { computed, ref } from "vue";
import { RouterLink, useRoute, useRouter } from "vue-router";
import { Layers, Pencil, Plus } from "lucide-vue-next";
import { Button } from "@wow-two-beta/fe/presentation/actions";
import { DataRegion } from "@wow-two-beta/fe/presentation/feedback";
import { PageHeaderActions } from "@wow-two-beta/fe/presentation/layout";
import { NavItem } from "@wow-two-beta/fe/presentation/nav";
import { ConsolePaths } from "@/application/navigation";
import { browse, usePortfolio, useProductReadings, workspaceChips, type PortfolioGroup } from "@/application/portfolio";
import { useRefresh } from "@/bootstrap/query";
import { PinKind } from "@/domain/pins";
import type { Product } from "@/domain/products";
import { RefreshButton } from "@/presentation/common/components";
import WorkspaceSection from "@/presentation/home/components/WorkspaceSection.vue";
import { PinToggle } from "@/presentation/pins";
import ProductFormModal from "../components/ProductFormModal.vue";
import WorkspacesModal from "../components/WorkspacesModal.vue";

/**
 * The products browser: workspaces as chips — All, then each workspace in the operator's order — over the chosen
 * workspace's product cards, each opening its product page. `/products` shows All; `/w/{workspace}` the one
 * workspace. New product and workspace management are page actions; each card edits its product. Inactive products
 * stay hidden until "Show inactive" is on.
 */
defineOptions({ name: "ProductsPage" });
const route = useRoute();
const router = useRouter();
const portfolio = usePortfolio();
const readings = useProductReadings();
const refresh = useRefresh(() =>
  Promise.all([portfolio.products.reload(), portfolio.workspaces.reload(), readings.refresh()]),
);
const productForm = ref<{ open: boolean; product: Product | null }>({ open: false, product: null });
const managingWorkspaces = ref(false);
/** The workspace the address chooses; null on `/products`. */
const workspaceSlug = computed(() => {
  const value = route.params["workspace"];
  const item = Array.isArray(value) ? value[0] : value;
  return typeof item === "string" && item ? item : null;
});
const firstLoad = computed(() => portfolio.isFirstLoad.value);
const chips = computed(() => workspaceChips(portfolio.groups.value));
const view = computed(() => browse(portfolio.groups.value, workspaceSlug.value));
/** Placeholder sections during the first load, so the page keeps its shape. */
const placeholders = computed<PortfolioGroup[]>(() =>
  (workspaceSlug.value ? [0] : [0, 1]).map(() => ({ workspace: null, products: [], total: 0, active: 0 })),
);
const sections = computed(() => (firstLoad.value ? placeholders.value : view.value.groups));
/** The workspace a new product starts in: the chosen one. */
const chosenWorkspaceId = computed(() => (workspaceSlug.value ? (view.value.groups[0]?.workspace?.id ?? null) : null));

/** Opens the product form: blank for a new product, or filled to edit one. @internal */
function openProductForm(product: Product | null): void {
  productForm.value = { open: true, product };
}
</script>

<template>
  <div class="flex flex-col gap-6">
    <PageHeaderActions>
      <RefreshButton size="md" icon-only :refreshing="refresh.refreshing.value" @refresh="refresh.refresh" />
      <Button variant="outline" tone="neutral" @click="managingWorkspaces = true">
        <template #leading><Layers :size="16" /></template>Workspaces
      </Button>
      <Button :is-disabled="productForm.open" @click="openProductForm(null)">
        <template #leading><Plus :size="16" /></template>New product
      </Button>
    </PageHeaderActions>

    <!-- The chips scroll sideways inside their own row on narrow screens; the page itself never does. -->
    <nav aria-label="Workspaces" class="-mx-4 min-w-0 px-4 md:mx-0 md:px-0">
      <ul role="list" class="flex gap-2 overflow-x-auto pb-1 [scrollbar-width:none]">
        <li v-for="chip in chips" :key="chip.slug ?? 'all'" class="shrink-0">
          <NavItem
            as-child
            size="sm"
            :is-active="chip.slug === workspaceSlug"
            class="h-9 w-auto rounded-full border border-border bg-card px-3.5 whitespace-nowrap data-[active]:border-primary/40 max-md:h-11"
          >
            <RouterLink :to="chip.path"
              >{{ chip.label
              }}<span class="ms-1.5 text-xs tabular-nums text-muted-foreground group-data-[active]:text-current">{{
                firstLoad ? "" : chip.count
              }}</span></RouterLink
            >
          </NavItem>
        </li>
      </ul>
    </nav>

    <DataRegion
      :is-first-load="firstLoad"
      :error="portfolio.error.value ?? (workspaceSlug ? portfolio.workspaces.error.value : null)"
      :has-data="portfolio.hasData.value && (!workspaceSlug || portfolio.workspaces.hasData.value)"
      :is-empty="portfolio.hasData.value && (view.missing || !portfolio.products.products.value.length)"
      :empty-title="view.missing ? 'This workspace is not in Wheelhouse' : 'Your portfolio starts with a product'"
      :empty-description="
        view.missing
          ? 'It may have been removed or renamed.'
          : 'Import the wow2 apps from the catalog, or add a product by hand.'
      "
      loading-label="Loading products"
      class="flex flex-col gap-8"
      @retry="refresh.refresh"
    >
      <template #emptyActions>
        <Button v-if="view.missing" as-child variant="outline"
          ><RouterLink :to="ConsolePaths.products">All products</RouterLink></Button
        >
        <template v-else>
          <Button as-child><RouterLink :to="ConsolePaths.catalog">Import from the catalog</RouterLink></Button>
          <Button variant="outline" @click="openProductForm(null)">New product</Button>
        </template>
      </template>
      <WorkspaceSection
        v-for="(group, index) in sections"
        :key="group.workspace?.id ?? `group-${index}`"
        :group="group"
        :readings="readings"
        :is-loading="firstLoad"
      >
        <template v-if="group.workspace" #actions>
          <PinToggle
            :subject="{ kind: PinKind.Workspace, workspaceId: group.workspace.id }"
            :label="group.workspace.name"
            :show-text="Boolean(workspaceSlug)"
          />
        </template>
        <template #card-actions="{ product }">
          <Button
            size="sm"
            variant="ghost"
            tone="neutral"
            class="size-8 px-0 max-md:size-11"
            :aria-label="`Edit ${product.name}`"
            :title="`Edit ${product.name}`"
            @click="openProductForm(product)"
            ><Pencil :size="14"
          /></Button>
        </template>
      </WorkspaceSection>
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
