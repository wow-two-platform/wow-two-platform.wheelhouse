<script setup lang="ts">
import { Alert, DataRegion, useSkeletonItems } from "@wow-two-beta/fe/presentation/feedback";

import {
  useDeploymentTargets,
  useReleaseArtifacts,
} from "@/application/deployments";
import { Panel } from "@/presentation/common/components";

import TargetRow from "./TargetRow.vue";

/** Renders all configured environments and their independent target state reads. */
defineOptions({ name: "TargetsPanel" });
const targets = useDeploymentTargets();
const releases = useReleaseArtifacts();
/** The targets, or placeholder rows until the first read arrives. */
const rows = useSkeletonItems(() => targets.data.value, 3);
</script>

<template>
  <!-- The panel has no refresh control, so a failed first load offers its own retry. -->
  <Panel
    title="Environments"
    description="Verified releases and deployment readiness across configured targets."
  >
    <Alert
      v-if="releases.error.value"
      class="mb-4"
      severity="warning"
      title="Release comparison unavailable"
      description="Drift against the latest published release shows once the catalog loads."
    />
    <DataRegion
      :is-first-load="targets.isFirstLoad.value"
      :is-refreshing="targets.isRefreshing.value"
      :error="targets.error.value"
      :has-data="Boolean(targets.data.value)"
      :is-empty="Boolean(targets.data.value) && !targets.data.value?.length"
      empty-title="No deployment environments"
      empty-description="No deployment environment has been configured."
      loading-label="Loading environments"
      @retry="targets.refetch()"
    >
      <ul class="divide-y divide-border">
        <TargetRow
          v-for="(target, index) in rows"
          :key="target?.id ?? `placeholder-${index}`"
          :target="target"
          :releases="releases.data.value ?? []"
        />
      </ul>
    </DataRegion>
  </Panel>
</template>
