<script lang="ts">
import type { JobStep } from "@/domain/infrastructure";

/** Defines the live steps of an enrollment or a placement. */
export interface JobStepsProps {
  /** The job's steps in order, as the host recorded them. */
  readonly steps: readonly JobStep[];
  /** The list's accessible name. */
  readonly label?: string;
}
</script>

<script setup lang="ts">
import { computed, onScopeDispose, shallowRef, watch } from "vue";
import { Check, CircleDashed, CircleMinus, LoaderCircle, X } from "lucide-vue-next";
import { Measures } from "@/domain/common";
import { InfraJobRules, JobStepStatus } from "@/domain/infrastructure";

/**
 * Draws each step with its outcome, detail and duration; a running step's elapsed time ticks every second while the
 * job runs. Titles come from the host and show as is.
 */
defineOptions({ name: "JobSteps" });
const props = withDefaults(defineProps<JobStepsProps>(), { label: "Steps" });
defineSlots<{}>();

const tone: Record<JobStepStatus, string> = {
  [JobStepStatus.Pending]: "text-muted-foreground",
  [JobStepStatus.Running]: "text-primary",
  [JobStepStatus.Succeeded]: "text-success",
  [JobStepStatus.Failed]: "text-destructive",
  [JobStepStatus.Skipped]: "text-muted-foreground",
};

/** The clock a running step's elapsed time reads; it ticks only while a step runs. @internal */
const now = shallowRef(Date.now());
let timer: ReturnType<typeof setInterval> | undefined;
const running = computed(() => InfraJobRules.hasRunningStep(props.steps));
watch(
  running,
  (value) => {
    clearInterval(timer);
    timer = undefined;
    now.value = Date.now();
    if (value) timer = setInterval(() => (now.value = Date.now()), 1000);
  },
  { immediate: true },
);
onScopeDispose(() => clearInterval(timer));

/** A step's duration text, or nothing before it starts. @internal */
function duration(step: JobStep): string | null {
  const seconds = InfraJobRules.stepSeconds(step, now.value);
  return seconds === null ? null : Measures.duration(seconds);
}
</script>

<template>
  <ol v-if="props.steps.length" class="flex flex-col gap-1.5" :aria-label="props.label">
    <li
      v-for="step in props.steps"
      :key="step.key"
      class="grid grid-cols-[1rem_minmax(0,1fr)_auto] items-start gap-x-2 text-sm"
      :class="step.status === JobStepStatus.Pending ? 'text-muted-foreground' : ''"
    >
      <span :class="['mt-0.5', tone[step.status]]" aria-hidden="true">
        <LoaderCircle
          v-if="step.status === JobStepStatus.Running"
          :size="16"
          class="animate-spin [animation-duration:2s] motion-reduce:animate-none"
        />
        <Check v-else-if="step.status === JobStepStatus.Succeeded" :size="16" />
        <X v-else-if="step.status === JobStepStatus.Failed" :size="16" />
        <CircleMinus v-else-if="step.status === JobStepStatus.Skipped" :size="16" />
        <CircleDashed v-else :size="16" />
      </span>
      <span class="min-w-0">
        <span :class="step.status === JobStepStatus.Pending ? '' : 'font-medium'">{{ step.title }}</span>
        <span class="sr-only">, {{ InfraJobRules.stepWord(step.status) }}</span>
        <span v-if="step.detail" class="block break-words text-muted-foreground">{{ step.detail }}</span>
      </span>
      <span v-if="duration(step)" class="font-mono text-xs text-muted-foreground tabular-nums">{{ duration(step) }}</span>
    </li>
  </ol>
</template>
