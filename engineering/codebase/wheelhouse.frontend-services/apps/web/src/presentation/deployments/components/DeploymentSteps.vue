<script lang="ts">
import type { DeploymentStep } from "@/domain/deployments";

/** Defines the steps of one rollout, as its target runner recorded them. */
export interface DeploymentStepsProps {
  /** The rollout's steps in order; the last one runs while the rollout does. */
  readonly steps?: readonly DeploymentStep[] | undefined;
}
</script>

<script setup lang="ts">
import { AlertTriangle, Check, CircleMinus, LoaderCircle, X } from "lucide-vue-next";

import { Measures } from "@/domain/common";
import { StepStatus } from "@/domain/deployments";

/** Renders each rollout step with its outcome, detail and duration, so the owner sees what the target did. */
defineOptions({ name: "DeploymentSteps" });
const props = defineProps<DeploymentStepsProps>();

const tone: Record<StepStatus, string> = {
  [StepStatus.Running]: "text-primary",
  [StepStatus.Succeeded]: "text-success",
  [StepStatus.Failed]: "text-destructive",
  [StepStatus.Warning]: "text-warning",
  [StepStatus.Skipped]: "text-muted-foreground",
};
const label: Record<StepStatus, string> = {
  [StepStatus.Running]: "running",
  [StepStatus.Succeeded]: "done",
  [StepStatus.Failed]: "failed",
  [StepStatus.Warning]: "needs a look",
  [StepStatus.Skipped]: "skipped",
};

function seconds(step: DeploymentStep) {
  if (!step.startedAt || !step.completedAt) return null;
  const elapsed = (Date.parse(step.completedAt) - Date.parse(step.startedAt)) / 1000;
  return Number.isFinite(elapsed) && elapsed >= 0 ? elapsed : null;
}
</script>

<template>
  <ol v-if="props.steps?.length" class="flex flex-col gap-1.5" aria-label="Rollout steps">
    <li
      v-for="(step, index) in props.steps"
      :key="index + step.name"
      class="grid grid-cols-[1rem_1fr_auto] items-start gap-x-2 text-sm"
    >
      <span :class="['mt-0.5', tone[step.status]]" aria-hidden="true">
        <LoaderCircle v-if="step.status === StepStatus.Running" :size="16" class="animate-spin" />
        <Check v-else-if="step.status === StepStatus.Succeeded" :size="16" />
        <X v-else-if="step.status === StepStatus.Failed" :size="16" />
        <AlertTriangle v-else-if="step.status === StepStatus.Warning" :size="16" />
        <CircleMinus v-else :size="16" />
      </span>
      <span class="min-w-0">
        <span class="font-medium">{{ step.name }}</span>
        <span class="sr-only">, {{ label[step.status] }}</span>
        <span v-if="step.detail" class="block text-muted-foreground">{{ step.detail }}</span>
      </span>
      <span v-if="seconds(step) !== null" class="font-mono text-xs text-muted-foreground tabular-nums">
        {{ Measures.duration(seconds(step)) }}
      </span>
    </li>
  </ol>
</template>
