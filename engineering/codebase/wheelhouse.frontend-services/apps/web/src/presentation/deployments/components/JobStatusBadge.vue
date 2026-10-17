<script lang="ts">
import type { JobStatus } from "@/domain/deployments";

/** Defines a deployment status badge. */
export interface JobStatusBadgeProps {
  /** The recorded submission status. */
  readonly status: JobStatus;
}
</script>

<script setup lang="ts">
import { Badge } from "@wow-two-beta/fe/presentation/display";

import { DeploymentExtensions } from "@/domain/deployments";

/** Renders a deployment status as a semantic badge. */
defineOptions({ name: "JobStatusBadge" });
const props = defineProps<JobStatusBadgeProps>();
</script>

<template>
  <Badge :variant="DeploymentExtensions.statusVariant(props.status)">
    {{ DeploymentExtensions.label(props.status) }}
  </Badge>
</template>
