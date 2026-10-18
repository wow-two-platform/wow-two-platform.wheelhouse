<script lang="ts">
import type { StatusFilter } from "@/domain/deployments";

/** Defines the calendar's info button and first-run intro. */
export interface ActivityGridInfoProps {
  /** The status filter: a filtered calendar's scale runs in that outcome's colour. */
  readonly status: StatusFilter;
  /** Whether the calendar has drawn its first read; the intro waits for it. */
  readonly isReady: boolean;
}
</script>

<script setup lang="ts">
import { computed } from "vue";

import { DeploymentFilters } from "@/domain/deployments";
import { InfoPopover, type InfoStep } from "@/presentation/common/components";

import { CellFills } from "./CellFills";

/**
 * The key to the deployment calendar behind an info button: what a square is, what its colour and marks say, and how
 * today and the chosen day are told apart. Shown once by itself, step by step, the first time the calendar renders.
 */
defineOptions({ name: "ActivityGridInfo" });
const props = defineProps<ActivityGridInfoProps>();

const today = DeploymentFilters.localDate(new Date());
const steps = computed<InfoStep[]>(() => [
  {
    key: "days",
    title: "One square per day",
    target: "[data-intro-target='calendar']",
  },
  {
    key: "outcomes",
    title: "Colour is the outcome",
    target: "[data-intro-target='calendar']",
  },
  {
    key: "marks",
    title: "Today and the chosen day",
    target: `[data-intro-target='calendar'] [data-date='${today}']`,
  },
]);
const scale = computed(() => CellFills[props.status === "failed" ? "danger" : "success"].slice(1));
</script>

<template>
  <InfoPopover title="Deployment calendar" :steps="steps" intro="deployments.calendar" :is-ready="props.isReady">
    <template #days>
      <p>Weeks run left to right, Monday on top. Point at a day for its counts; choose it to list its deployments.</p>
    </template>
    <template #outcomes>
      <ul class="flex flex-col gap-1.5" aria-label="Colours">
        <li class="flex items-center gap-2">
          <span class="size-3.5 shrink-0 rounded-[3px] bg-success" aria-hidden="true" />Every rollout succeeded
        </li>
        <li class="flex items-center gap-2">
          <span
            class="flex size-3.5 shrink-0 items-center justify-center rounded-[3px] bg-destructive"
            aria-hidden="true"
            ><span class="size-1.5 rounded-full bg-destructive-foreground/90"
          /></span>
          Every rollout failed — a dot marks it
        </li>
        <li class="flex items-center gap-2">
          <span class="flex size-3.5 shrink-0 items-center justify-center rounded-[3px] bg-warning" aria-hidden="true"
            ><span class="h-0.5 w-2 rounded-full bg-warning-foreground/90"
          /></span>
          Some succeeded, some failed — a dash marks it
        </li>
      </ul>
      <p class="mt-2.5 flex flex-wrap items-center gap-x-1.5 gap-y-1">
        <span>A deeper fill means more:</span>
        <span class="flex items-center gap-1 text-xs text-muted-foreground" aria-hidden="true">
          1<span v-for="(fill, index) in scale" :key="index" :class="['size-3.5 rounded-[3px]', fill]" />7+
        </span>
        <span class="sr-only">1, 2 to 3, 4 to 6, and 7 or more deployments.</span>
      </p>
    </template>
    <template #marks>
      <ul class="flex flex-col gap-1.5">
        <li class="flex items-center gap-2.5">
          <span class="relative size-3.5 shrink-0 rounded-[3px] bg-muted" aria-hidden="true"
            ><span class="wh-key-today"
          /></span>
          Today: a dashed ring, pulsing while a rollout runs
        </li>
        <li class="flex items-center gap-2.5">
          <span
            class="size-3.5 shrink-0 rounded-[3px] bg-muted ring-2 ring-primary ring-offset-1 ring-offset-card"
            aria-hidden="true"
          />
          The chosen day: a solid ring
        </li>
      </ul>
    </template>
  </InfoPopover>
</template>

<style scoped>
/* The calendar's today ring, drawn the same way on the key's swatch. */
.wh-key-today {
  position: absolute;
  inset: 0;
  border-radius: 3px;
  outline: 1.5px dashed var(--color-muted-foreground);
  outline-offset: 1.5px;
}
</style>
