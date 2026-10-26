<script lang="ts">
import type { DeploymentStep } from "@/domain/deployments";

/** Defines the steps of one rollout, as its target runner recorded them. */
export interface DeploymentStepsProps {
  /** The rollout's steps in order; the last one runs while the rollout does. */
  readonly steps?: readonly DeploymentStep[] | undefined;
  /** Cuts the steps where changes start: the checks before, the changes, and what follows once the services run. */
  readonly isSectioned?: boolean;
}
</script>

<script setup lang="ts">
import { computed } from "vue";
import { AlertTriangle, Check, CircleMinus, LoaderCircle, Undo2, X } from "lucide-vue-next";

import { Measures } from "@/domain/common";
import { DeploymentStates, StepKinds, StepStatus } from "@/domain/deployments";
import { StepKindIcons } from "./StepKindIcons";

/**
 * Renders each rollout step with its outcome, a quiet mark of its kind, its detail and duration, so the owner sees what
 * the target did. The kind's mark is decorative; the outcome stays an icon with its word for assistive technology. Sectioned,
 * the checks before any change come first — a cancel there stops cleanly — then a boundary, "Changes start here",
 * after which a cancel is refused; a roll back reads as its own nested step; then what follows once the services run.
 */
defineOptions({ name: "DeploymentSteps" });
const props = withDefaults(defineProps<DeploymentStepsProps>(), { isSectioned: false });

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

/** The groups to draw: one untitled group, or the three sections with their titles. @internal */
const groups = computed(() => {
  const steps = props.steps ?? [];
  if (!props.isSectioned) return [{ key: "all", title: null as string | null, steps, boundary: false }];
  const sections = DeploymentStates.sections(steps);
  return [
    { key: "before", title: sections.changes.length ? "Before changes — a cancel stops cleanly" : null, steps: sections.before, boundary: false },
    { key: "changes", title: null, steps: sections.changes, boundary: sections.changes.length > 0 },
    { key: "after", title: "After changes", steps: sections.after, boundary: false },
  ].filter((group) => group.steps.length > 0);
});

function seconds(step: DeploymentStep) {
  if (!step.startedAt || !step.completedAt) return null;
  const elapsed = (Date.parse(step.completedAt) - Date.parse(step.startedAt)) / 1000;
  return Number.isFinite(elapsed) && elapsed >= 0 ? elapsed : null;
}

/** A roll back reads as a nested step under the failure it answers. @internal */
const isRollBack = (step: DeploymentStep) => step.name.startsWith("Roll back") || step.name.startsWith("Restore ");
</script>

<template>
  <div v-if="props.steps?.length" class="flex flex-col gap-3">
    <template v-for="group in groups" :key="group.key">
      <p
        v-if="group.boundary"
        class="flex items-center gap-3 text-xs font-medium text-warning-soft-foreground"
        role="separator"
        aria-label="Changes start here; a cancel is refused from now on"
      >
        <span class="h-px flex-1 border-t border-dashed border-warning/60" aria-hidden="true" />
        <span class="min-w-0 text-center">Changes start here — a cancel is refused from now on</span>
        <span class="h-px flex-1 border-t border-dashed border-warning/60" aria-hidden="true" />
      </p>
      <section class="flex flex-col gap-1.5" :aria-label="group.title ?? 'Rollout steps'">
        <h4 v-if="group.title" class="text-xs font-semibold tracking-wide text-muted-foreground uppercase">{{ group.title }}</h4>
        <ol class="flex flex-col gap-1.5">
          <li
            v-for="(step, index) in group.steps"
            :key="index + step.name"
            :class="[
              'grid grid-cols-[1rem_0.875rem_minmax(0,1fr)_auto] items-start gap-x-2 text-sm',
              isRollBack(step) ? 'ms-4 border-s border-border ps-3' : '',
            ]"
          >
            <span :class="['mt-0.5', tone[step.status]]" aria-hidden="true">
              <LoaderCircle v-if="step.status === StepStatus.Running" :size="16" class="animate-spin" />
              <Undo2 v-else-if="isRollBack(step) && step.status === StepStatus.Succeeded" :size="16" />
              <Check v-else-if="step.status === StepStatus.Succeeded" :size="16" />
              <X v-else-if="step.status === StepStatus.Failed" :size="16" />
              <AlertTriangle v-else-if="step.status === StepStatus.Warning" :size="16" />
              <CircleMinus v-else :size="16" />
            </span>
            <component
              :is="StepKindIcons[StepKinds.of(step.name)]"
              :size="14"
              class="mt-[3px] text-muted-foreground"
              aria-hidden="true"
            />
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
      </section>
    </template>
  </div>
</template>
