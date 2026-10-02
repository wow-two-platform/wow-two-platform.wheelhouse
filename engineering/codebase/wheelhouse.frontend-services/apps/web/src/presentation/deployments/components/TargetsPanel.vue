<script setup lang="ts">
import { Alert } from "@wow-two-beta/ui-vue/presentation/feedback";

import { useRefresh } from "@/bootstrap/query";
import {
  useDeploymentTargets,
  useReleaseArtifacts,
} from "@/application/deployments";
import { LoadState, Panel } from "@/presentation/common/components";

import TargetRow from "./TargetRow.vue";

/** Renders all configured environments and their independent target state reads. */
defineOptions({ name: "TargetsPanel" });
const targets = useDeploymentTargets();
const targetRefresh = useRefresh(targets.refetch);
const releases = useReleaseArtifacts();
</script>

<template>
  <Panel
    title="Environments"
    description="Verified releases and deployment readiness across configured targets."
  >
    <Alert
      v-if="releases.error.value"
      class="mb-4"
      severity="warning"
      title="Release comparison unavailable"
      :description="releases.error.value.message"
    />
    <LoadState
      :loading="targets.loading.value && !targets.data.value"
      :refreshing="targetRefresh.refreshing.value"
      :error="targets.error.value"
      :has-data="Boolean(targets.data.value)"
      :empty="!targets.data.value?.length"
      empty-title="No deployment environments"
      empty-description="No deployment environment has been configured."
      @retry="targetRefresh.refresh()"
    >
      <ul class="divide-y divide-border">
        <TargetRow
          v-for="target in targets.data.value"
          :key="target.id"
          :target="target"
          :releases="releases.data.value ?? []"
        />
      </ul>
    </LoadState>
  </Panel>
</template>
