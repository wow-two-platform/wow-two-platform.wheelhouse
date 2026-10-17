<script setup lang="ts">
import { computed } from 'vue';
import { RouterLink } from 'vue-router';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { DataRegion } from '@wow-two-beta/fe/presentation/feedback';
import { PageHeaderActions } from '@wow-two-beta/fe/presentation/layout';
import { ConsolePaths } from '@/application/navigation';
import { usePortfolio, useProductReadings, type PortfolioGroup } from '@/application/portfolio';
import { useRefresh } from '@/bootstrap/query';
import { PinKind } from '@/domain/pins';
import { RefreshButton } from '@/presentation/common/components';
import { PinToggle } from '@/presentation/pins';
import AttentionSummary from '../components/AttentionSummary.vue';
import PinnedStrip from '../components/PinnedStrip.vue';
import WorkspaceSection from '../components/WorkspaceSection.vue';

/**
 * The portfolio home: what needs attention, the pins, then every workspace in the operator's order with its products'
 * dev and prod health and last deploy. Inactive products stay hidden until "Show inactive" is on.
 */
defineOptions({ name: 'HomePage' });
const portfolio = usePortfolio();
const readings = useProductReadings();
const refresh = useRefresh(() =>
  Promise.all([portfolio.products.reload(), portfolio.workspaces.reload(), readings.refresh()]),
);
/** Two placeholder workspaces during the first load, so the page keeps its shape. */
const placeholders: PortfolioGroup[] = [0, 1].map(() => ({ workspace: null, products: [], total: 0, active: 0 }));
const groups = computed(() => (portfolio.isFirstLoad.value ? placeholders : portfolio.groups.value));
</script>
<template>
  <div class="flex flex-col gap-8">
    <PageHeaderActions>
      <RefreshButton size="md" icon-only :refreshing="refresh.refreshing.value" @refresh="refresh.refresh" />
    </PageHeaderActions>
    <AttentionSummary />
    <!-- Phones lead with the pins: the sidebar that also lists them is absent there. -->
    <PinnedStrip class="max-md:-order-1" />
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
        :readings="readings"
        :is-loading="portfolio.isFirstLoad.value"
      >
        <template v-if="group.workspace" #actions>
          <PinToggle :subject="{ kind: PinKind.Workspace, workspaceId: group.workspace.id }" :label="group.workspace.name" />
        </template>
      </WorkspaceSection>
    </DataRegion>
  </div>
</template>
