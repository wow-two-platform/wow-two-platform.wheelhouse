<script setup lang="ts">
import { Rocket } from "lucide-vue-next";

import { Button } from "@wow-two-beta/fe/presentation/actions";
import { DataRegion, SkeletonStateGroup } from "@wow-two-beta/fe/presentation/feedback";
import { PageHeaderActions } from "@wow-two-beta/fe/presentation/layout";

import { useRefresh } from "@/bootstrap/query";
import { useDeploymentHistory } from "@/application/deployments";
import type { DeploymentJob } from "@/domain/deployments";
import { Panel, RefreshButton } from "@/presentation/common/components";

import DeployModal from "../components/DeployModal.vue";
import DeploymentStatsPanel from "../components/DeploymentStatsPanel.vue";
import HistoryTable from "../components/HistoryTable.vue";
import ReleaseCatalogPanel from "../components/ReleaseCatalogPanel.vue";
import TargetsPanel from "../components/TargetsPanel.vue";
import { useDeployModal } from "../hooks/useDeployModal";

/** Renders portfolio deployment history, complete statistics and target operations. */
defineOptions({ name: "DeploymentsPage" });
const history = useDeploymentHistory();
const historyRefresh = useRefresh(history.refetch);
const deploy = useDeployModal();

/** Preselects the exact successful submission's target and bundle for a new confirmation. */
function redeploy(job: DeploymentJob): void {
  if (job.targetId && job.bundleId)
    deploy.openDeploy({ target: job.targetId, release: job.bundleId });
}
</script>

<template>
  <div class="flex flex-col gap-6">
    <PageHeaderActions>
      <Button variant="solid" tone="primary" @click="deploy.openDeploy()">
        <template #leading><Rocket :size="16" /></template>Deploy
      </Button>
    </PageHeaderActions>
    <TargetsPanel />
    <Panel
      title="Recent deployments"
      description="The latest 50 submissions across all products and environments."
    >
      <template #actions>
        <RefreshButton
          variant="ghost"
          :refreshing="historyRefresh.refreshing.value"
          @refresh="historyRefresh.refresh()"
        />
      </template>
      <DataRegion
        :is-first-load="history.isFirstLoad.value"
        :is-refreshing="history.isRefreshing.value"
        :error="history.error.value"
        :has-data="Boolean(history.data.value)"
        :is-empty="Boolean(history.data.value) && !history.data.value?.length"
        empty-title="No deployments yet"
        loading-label="Loading deployments"
      >
        <SkeletonStateGroup class="contents" :is-loading="historyRefresh.refreshing.value">
          <HistoryTable :jobs="history.data.value" :on-redeploy="redeploy" />
        </SkeletonStateGroup>
      </DataRegion>
    </Panel>
    <ReleaseCatalogPanel
      @deploy="deploy.openDeploy({ target: '', release: $event })"
    />
    <DeploymentStatsPanel show-targets />
    <DeployModal
      :key="deploy.session.value"
      :open="deploy.open.value"
      :selection="deploy.selection.value"
      @update:open="deploy.onOpenChange"
    />
  </div>
</template>
