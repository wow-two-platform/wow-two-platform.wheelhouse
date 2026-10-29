<script setup lang="ts">
import { computed, defineAsyncComponent, provide, ref } from 'vue';
import { RouterLink } from 'vue-router';
import { Button, RefreshButton } from '@wow-two-beta/fe/presentation/actions';
import { DataRegion } from '@wow-two-beta/fe/presentation/feedback';
import { PageHeaderActions } from '@wow-two-beta/fe/presentation/layout';
import { ConsolePaths } from '@/application/navigation';
import { usePins } from '@/application/pins';
import { TrendScalesKey, useProductFacts, usePortfolio, type PortfolioGroup } from '@/application/portfolio';
import { useRefresh } from '@/bootstrap/query';
import { PinKind } from '@/domain/pins';
import type { Product } from '@/domain/products';
import AttentionSummary from '../components/AttentionSummary.vue';
import PinnedStrip from '../components/PinnedStrip.vue';
import WorkspaceSection from '../components/WorkspaceSection.vue';

/** The product form loads the first time a card's menu asks to edit; browsing never needs it. @internal */
const ProductFormModal = defineAsyncComponent(() => import('@/presentation/products/components/ProductFormModal.vue'));

/**
 * The portfolio home: what needs attention, then every workspace in the owner's order with its products as the
 * Products page draws them — environment pills and deploy trends. Pinned products live in the sidebar; phones, which
 * have none, keep a compact strip of them at the top. Inactive products stay hidden until "Show inactive" is on.
 */
defineOptions({ name: 'HomePage' });
const portfolio = usePortfolio();
const facts = useProductFacts();
const pins = usePins();
const refresh = useRefresh(() =>
  Promise.all([portfolio.products.reload(), portfolio.workspaces.reload(), facts.refresh()]),
);
const productForm = ref<{ open: boolean; product: Product | null }>({ open: false, product: null });
/** True from the first edit; until then the form's code is not loaded. @internal */
const formWanted = computed(() => productForm.value.open || productForm.value.product !== null);
/* Every trend on the page shares one scale, as on the Products page. */
provide(TrendScalesKey, facts.scales);
/** Two placeholder workspaces during the first load, so the page keeps its shape. */
const placeholders: PortfolioGroup[] = [0, 1].map(() => ({ workspace: null, products: [], total: 0, active: 0 }));
const groups = computed(() => (portfolio.isFirstLoad.value ? placeholders : portfolio.groups.value));

/** Whether a product is pinned; the pin shows on its card. @internal */
function isPinned(product: Product): boolean {
  return pins.isPinned({ kind: PinKind.Product, product });
}
</script>
<template>
  <div class="flex flex-col gap-8">
    <PageHeaderActions>
      <RefreshButton size="md" is-icon-only :is-refreshing="refresh.refreshing.value" @click="refresh.refresh" />
    </PageHeaderActions>
    <!-- Phones lead with the pins: the sidebar that lists them is absent there. -->
    <PinnedStrip class="md:hidden" />
    <AttentionSummary />
    <DataRegion
      :is-first-load="portfolio.isFirstLoad.value"
      :error="portfolio.error.value"
      :has-data="portfolio.hasData.value"
      :is-empty="portfolio.hasData.value && !portfolio.products.products.value.length"
      empty-title="Your portfolio starts with a product"
      empty-description="Import the wow2 apps from the catalog, or add a product by hand."
      loading-label="Loading the portfolio"
      class="flex flex-col gap-8"
      @retry="refresh.refresh"
    >
      <template #emptyActions>
        <Button as-child><RouterLink :to="ConsolePaths.catalog">Import from the catalog</RouterLink></Button>
        <Button as-child variant="outline"><RouterLink :to="ConsolePaths.products">Add product</RouterLink></Button>
      </template>
      <WorkspaceSection
        v-for="(group, index) in groups"
        :key="group.workspace?.id ?? `group-${index}`"
        :group="group"
        :facts-of="facts.factsOf"
        :weeks="facts.weeks.value"
        :is-pinned="isPinned"
        :is-loading="portfolio.isFirstLoad.value"
        @edit="(product) => (productForm = { open: true, product })"
      />
    </DataRegion>
    <ProductFormModal
      v-if="formWanted"
      :open="productForm.open"
      :product="productForm.product"
      :operations="portfolio.products"
      :workspaces="portfolio.workspaces.workspaces.value"
      @update:open="(open) => (productForm = { ...productForm, open })"
    />
  </div>
</template>
