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
import { CircleCheck, CircleDashed, CircleX, LoaderCircle, TriangleAlert, Undo2 } from "lucide-vue-next";

import { Measures } from "@/domain/common";
import { DeploymentStates, StepFindingKind, StepKind, StepKinds, StepStatus } from "@/domain/deployments";
import type { StepFinding } from "@/domain/deployments";
import { StepKindIcons } from "./StepKindIcons";

/**
 * Renders each rollout step as a timeline: its outcome as the node on a line joining the steps, then its name led by a
 * quiet glyph of its kind, its detail one shade dimmer, and its duration, so the owner sees what the target did. A
 * config check lists each finding under its detail — what is wrong, the setting to fix and the rule's code — and never
 * a value. The
 * outcome is the only circle on the line; the kind's glyph sits small in the text and is decorative. A skipped step
 * says so in words, with a dashed node and its name dimmed: it had nothing to do. Sectioned,
 * the checks before any change come first — a cancel there stops cleanly — then a boundary, "Changes start here",
 * after which a cancel is refused; a roll back reads as its own nested step, as does a rollback gate that follows a
 * failure, part of the same automatic restore; then what follows once the services run.
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
  [StepStatus.Skipped]: "skipped, nothing to do",
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

/** The automatic restore's steps, nested under the failure they answer. @internal */
const restore = computed(() => StepKinds.restoreSteps(props.steps ?? []));
/** A restore step that put something back, marked as undone rather than done; a rollback gate only checks. @internal */
const isRollBack = (step: DeploymentStep) => {
  const kind = StepKinds.of(step.name);
  return restore.value.has(step) && (kind === StepKind.RollBack || kind === StepKind.Restore);
};

/** What each finding says is wrong, as its line opens. @internal */
const findingWords: Record<StepFindingKind, string> = {
  [StepFindingKind.Invalid]: "Invalid",
  [StepFindingKind.Unknown]: "Unknown key",
  [StepFindingKind.Stranded]: "Strands people",
};
/** A stranding finding's count in words. @internal */
const stranded = (finding: StepFinding) =>
  finding.count === null ? null : `${finding.count} ${finding.count === 1 ? "person" : "people"} left no way in`;
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
        <ol class="flex flex-col">
          <li
            v-for="(step, index) in group.steps"
            :key="index + step.name"
            :class="[
              'relative grid grid-cols-[1.25rem_minmax(0,1fr)_auto] items-start gap-x-2.5 text-sm',
              index < group.steps.length - 1 ? 'pb-3' : '',
              restore.has(step) ? 'ps-6' : '',
            ]"
            :data-status="step.status"
          >
            <!-- The line to the next step's node; a restore step hangs off it, nested under the failure it answers. -->
            <span
              v-if="restore.has(step)"
              class="absolute start-[calc(0.625rem-0.5px)] top-0 h-2.5 w-3.5 rounded-es-md border-s border-b border-border-strong"
              aria-hidden="true"
            />
            <span
              v-if="index < group.steps.length - 1"
              :class="[
                'absolute start-[calc(0.625rem-0.5px)] bottom-0 w-px bg-border-strong',
                restore.has(step) ? 'top-0' : 'top-6',
              ]"
              aria-hidden="true"
            />
            <span :class="['flex size-5 items-center justify-center', tone[step.status]]" aria-hidden="true">
              <LoaderCircle v-if="step.status === StepStatus.Running" :size="16" class="animate-spin motion-reduce:animate-none" />
              <Undo2 v-else-if="isRollBack(step) && step.status === StepStatus.Succeeded" :size="16" />
              <CircleCheck v-else-if="step.status === StepStatus.Succeeded" :size="16" />
              <CircleX v-else-if="step.status === StepStatus.Failed" :size="16" />
              <TriangleAlert v-else-if="step.status === StepStatus.Warning" :size="16" />
              <CircleDashed v-else :size="16" />
            </span>
            <span class="min-w-0">
              <span class="flex min-w-0 flex-wrap items-center gap-x-1.5 gap-y-0.5 leading-5">
                <component
                  :is="StepKindIcons[StepKinds.of(step.name)]"
                  :size="13"
                  class="shrink-0 text-subtle-foreground"
                  aria-hidden="true"
                />
                <span :class="['font-medium', step.status === StepStatus.Skipped ? 'text-muted-foreground' : '']">{{
                  step.name
                }}</span>
                <span
                  v-if="step.status === StepStatus.Skipped"
                  class="rounded-full border border-dashed border-border px-1.5 text-xs leading-4 text-muted-foreground"
                  aria-hidden="true"
                  >skipped</span
                >
                <span class="sr-only">, {{ label[step.status] }}</span>
              </span>
              <span v-if="step.detail" class="mt-0.5 block text-xs text-subtle-foreground">{{ step.detail }}</span>
              <ul
                v-if="step.findings?.length"
                class="mt-1 flex flex-col gap-0.5 text-xs"
                :aria-label="`What ${step.name} found`"
              >
                <li
                  v-for="(finding, at) in step.findings"
                  :key="at"
                  class="flex min-w-0 flex-wrap items-baseline gap-x-1.5 gap-y-0.5"
                >
                  <span class="text-muted-foreground">{{ findingWords[finding.kind] }}</span>
                  <code class="min-w-0 font-mono break-all text-foreground">{{ finding.setting ?? "an unnamed setting" }}</code>
                  <span
                    v-if="finding.code"
                    class="rounded border border-border px-1 font-mono leading-4 text-subtle-foreground"
                    :title="`Rule ${finding.code}`"
                    >{{ finding.code }}</span
                  >
                  <span v-if="stranded(finding)" class="text-subtle-foreground">{{ stranded(finding) }}</span>
                </li>
              </ul>
            </span>
            <span v-if="seconds(step) !== null" class="font-mono text-xs leading-5 text-muted-foreground tabular-nums">
              {{ Measures.duration(seconds(step)) }}
            </span>
          </li>
        </ol>
      </section>
    </template>
  </div>
</template>
