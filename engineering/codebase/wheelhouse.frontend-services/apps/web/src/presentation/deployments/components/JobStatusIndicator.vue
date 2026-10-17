<script lang="ts">
import type { JobStatus } from "@/domain/deployments";

/** Defines a compact deployment status and optional reason. */
export interface JobStatusIndicatorProps {
  /** The submission's current status. */
  readonly status: JobStatus;
  /** The operator-safe explanation of its outcome. */
  readonly reason?: string | null | undefined;
}
</script>

<script setup lang="ts">
import { StatusIndicator } from "@wow-two-beta/fe/presentation/feedback";

import { DeploymentExtensions } from "@/domain/deployments";

/** Renders a compact status indicator with a pulse while the submission is pending. */
defineOptions({ name: "JobStatusIndicator" });
const props = defineProps<JobStatusIndicatorProps>();
</script>

<template>
  <StatusIndicator
    :tone="DeploymentExtensions.statusTone(props.status)"
    :label="DeploymentExtensions.label(props.status)"
    :has-pulse="DeploymentExtensions.isPending(props.status)"
    v-bind="props.reason ? { description: props.reason } : {}"
  />
</template>
