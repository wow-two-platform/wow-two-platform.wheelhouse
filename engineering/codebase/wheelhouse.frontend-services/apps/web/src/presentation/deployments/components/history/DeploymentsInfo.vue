<script lang="ts">
import type { StatusFilter } from "@/domain/deployments";

/** Defines the deployments panel's info button. */
export interface DeploymentsInfoProps {
  /** The status filter: a filtered calendar's scale runs in that outcome's colour. */
  readonly status: StatusFilter;
  /** Whether the calendar shows; the List view needs only the statuses. */
  readonly isGrid: boolean;
}
</script>

<script setup lang="ts">
import { computed } from "vue";

import { StatusState, StatusWords } from "@/domain/common";
import { InfoPopover, StatusPill, type InfoSection } from "@/presentation/common/components";

import { CellFills } from "./CellFills";

/**
 * What the deployments panel's info button holds: the calendar's key — what a square is and how to move between days,
 * what its colour and marks say, how today and the chosen day are told apart — while the calendar shows, and always
 * the statuses a deployment's pill and the day's counts use.
 */
defineOptions({ name: "DeploymentsInfo" });
const props = defineProps<DeploymentsInfoProps>();

const CalendarSections: readonly InfoSection[] = [
  { key: "days", title: "One square per day" },
  { key: "outcomes", title: "Colour is the outcome" },
  { key: "marks", title: "Today and the chosen day" },
];
const StatusSection: InfoSection = { key: "statuses", title: "Statuses" };
const sections = computed(() => (props.isGrid ? [...CalendarSections, StatusSection] : [StatusSection]));
const scale = computed(() => CellFills[props.status === "failed" ? "danger" : "success"].slice(1));

/** Each status a deployment can end in, and what it means. @internal */
const Statuses: ReadonlyArray<{ readonly state: StatusState; readonly text: string }> = [
  { state: StatusState.Succeeded, text: "Finished as intended." },
  { state: StatusState.Failed, text: "Failed, or restoring the previous release failed too." },
  { state: StatusState.Degraded, text: "Rolled back to the previous release, or regressed while watched; the row names the cause." },
  { state: StatusState.Interrupted, text: "Stopped mid-way, then reconciled." },
  { state: StatusState.Deploying, text: "Submitted, queued or running." },
  { state: StatusState.Refused, text: "Refused by the runner before it changed anything." },
];
</script>

<template>
  <InfoPopover title="Deployments" :sections="sections">
    <template #days>
      <p>
        Weeks run left to right, Monday on top; finished rollouts count on the day they were submitted. Point at a day
        for its counts, choose it to list its deployments, and move between days with the arrow keys.
      </p>
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
      <p class="mt-2.5">Rolled back and interrupted rollouts count as failed here.</p>
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
    <template #statuses>
      <ul class="flex flex-col gap-2" aria-label="Statuses">
        <li v-for="item in Statuses" :key="item.state" class="flex items-start gap-2.5">
          <span class="w-28 shrink-0"><StatusPill :state="item.state" :label="StatusWords[item.state]" /></span
          ><span class="pt-0.5 text-xs text-muted-foreground">{{ item.text }}</span>
        </li>
      </ul>
      <p class="mt-2.5 text-xs text-muted-foreground">A chosen day's tiles count each status that occurred that day.</p>
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
