<script lang="ts">
import type { JobStatus } from "@/domain/deployments";

/** Defines a deployment status badge. */
export interface JobStatusBadgeProps {
  /** The recorded submission status. */
  readonly status: JobStatus;
}
</script>

<script setup lang="ts">
import DeploymentStatusPill from "./DeploymentStatusPill.vue";

/** Renders a deployment status as the console's status pill, without its cause. */
defineOptions({ name: "JobStatusBadge" });
const props = defineProps<JobStatusBadgeProps>();
</script>

<template>
  <DeploymentStatusPill :status="props.status" :has-cause="false" />
</template>
