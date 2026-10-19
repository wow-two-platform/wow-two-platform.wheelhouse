<script lang="ts">
import type { Attention, DeployState } from "@/domain/common";
import type { DeploymentStateCause, DeploymentWatch, JobStatus } from "@/domain/deployments";

/** Defines a deployment's status pill: what `DeploymentStates` reads, and whether the cause is written out. */
export interface DeploymentStatusPillProps {
  readonly status: JobStatus;
  /** The state the API read; without it the runner status is read the old way. */
  readonly state?: DeployState | null | undefined;
  readonly substate?: string | null | undefined;
  readonly attention?: Attention | null | undefined;
  readonly cause?: DeploymentStateCause | null | undefined;
  /** The display-safe explanation of its outcome, the cause when the API names none. */
  readonly reason?: string | null | undefined;
  /** The watch after a successful rollout: a regression turns the success degraded. */
  readonly watch?: Pick<DeploymentWatch, "status" | "findings"> | null | undefined;
  /** Writes the cause under the chip. Default `true`. */
  readonly hasCause?: boolean;
}
</script>

<script setup lang="ts">
import { computed } from "vue";

import { DeploymentStates } from "@/domain/deployments";

import DeploymentStateChip from "./DeploymentStateChip.vue";

/**
 * A deployment's state chip from its fields: the API's state where it carries one, else its runner status — a runner
 * job, the deploy dialog's follow-up — read the old way. The same chip as every deployment list.
 */
defineOptions({ name: "DeploymentStatusPill" });
const props = withDefaults(defineProps<DeploymentStatusPillProps>(), { hasCause: true });
defineSlots<{}>();

const reading = computed(() => DeploymentStates.of(props));
</script>

<template>
  <DeploymentStateChip :reading="reading" :has-cause="props.hasCause" />
</template>
