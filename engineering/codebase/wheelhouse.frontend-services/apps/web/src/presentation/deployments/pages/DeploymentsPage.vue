<script setup lang="ts">
import { Rocket } from "lucide-vue-next";

import { Button } from "@wow-two-beta/ui-vue/presentation/actions";

import { useRefresh } from "@/bootstrap/query";
import { useDeploymentHistory } from "@/application/deployments";
import type { DeploymentJob } from "@/domain/deployments";
import {
  LoadState,
  PageActions,
  Panel,
} from "@/presentation/common/components";

import DeployModal from "../components/DeployModal.vue";
import DeploymentStatsPanel from "../components/DeploymentStatsPanel.vue";
import HistoryTable from "../components/HistoryTable.vue";
import HistoryTableSkeleton from "../components/HistoryTableSkeleton.vue";
import ReleaseCatalogPanel from "../components/ReleaseCatalogPanel.vue";
import TargetsPanel from "../components/TargetsPanel.vue";
import { useDeployModal } from "../hooks/useDeployModal";
import { RefreshButton } from "@/presentation/common/components";

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
    <PageActions>
      <Button variant="solid" tone="primary" @click="deploy.openDeploy()">
        <template #leading><Rocket :size="16" /></template>Deploy
      </Button>
    </PageActions>
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
      <LoadState
        :loading="history.loading.value && !history.data.value"
        :refreshing="historyRefresh.refreshing.value"
        :error="history.error.value"
        :has-data="Boolean(history.data.value)"
        :empty="!history.data.value?.length"
        empty-title="No deployments yet"
        @retry="historyRefresh.refresh()"
      >
        <template #skeleton><HistoryTableSkeleton /></template>
        <HistoryTable
          :jobs="history.data.value ?? []"
          :on-redeploy="redeploy"
        />
      </LoadState>
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
