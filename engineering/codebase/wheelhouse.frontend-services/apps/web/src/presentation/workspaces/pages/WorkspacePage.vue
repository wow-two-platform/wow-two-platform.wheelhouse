<script setup lang="ts">
import { computed } from 'vue';
import { RouterLink, useRoute } from 'vue-router';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { DataRegion } from '@wow-two-beta/fe/presentation/feedback';
import { PageHeaderActions } from '@wow-two-beta/fe/presentation/layout';
import { ConsolePaths } from '@/application/navigation';
import { usePortfolio, useProductReadings, type PortfolioGroup } from '@/application/portfolio';
import { useRefresh } from '@/bootstrap/query';
import { RefreshButton } from '@/presentation/common/components';
import WorkspaceSection from '@/presentation/home/components/WorkspaceSection.vue';

/** One workspace's products, each with its environments' health and last deploy. */
defineOptions({ name: 'WorkspacePage' });
const route = useRoute();
const portfolio = usePortfolio();
const readings = useProductReadings();
const refresh = useRefresh(() =>
  Promise.all([portfolio.products.reload(), portfolio.workspaces.reload(), readings.refresh()]),
);
const slug = computed(() => {
  const value = route.params['workspace'];
  return typeof value === 'string' ? value : (value?.[0] ?? '');
});
const group = computed(() => portfolio.groups.value.find((item) => item.workspace?.slug === slug.value) ?? null);
/** A placeholder section during the first load, so the page keeps its shape. */
const placeholder: PortfolioGroup = { workspace: null, products: [], total: 0, active: 0 };
const firstLoad = computed(() => portfolio.isFirstLoad.value);
</script>
<template>
  <div>
    <PageHeaderActions>
      <RefreshButton size="md" icon-only :refreshing="refresh.refreshing.value" @refresh="refresh.refresh" />
    </PageHeaderActions>
    <DataRegion
      :is-first-load="firstLoad"
      :error="portfolio.error.value ?? portfolio.workspaces.error.value"
      :has-data="portfolio.hasData.value && portfolio.workspaces.hasData.value"
      :is-empty="portfolio.hasData.value && portfolio.workspaces.hasData.value && !group"
      empty-title="This workspace is not in Wheelhouse"
      empty-description="It may have been removed or renamed."
      loading-label="Loading the workspace"
      @retry="refresh.refresh"
    >
      <template #emptyActions>
        <Button as-child variant="outline"><RouterLink :to="ConsolePaths.home">Home</RouterLink></Button>
      </template>
      <WorkspaceSection
        :group="group ?? placeholder"
        :readings="readings"
        :is-loading="firstLoad"
      />
    </DataRegion>
  </div>
</template>
