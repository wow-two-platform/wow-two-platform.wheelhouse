<script lang="ts">
import type { StatusFilter } from "@/domain/deployments";

/** Defines the deployments panel's info button. */
export interface DeploymentsInfoProps {
  /** The status filter: a filtered calendar's scale runs in that attention's colour. */
  readonly status: StatusFilter;
  /** Whether the calendar shows; the List view needs only the statuses. */
  readonly isGrid: boolean;
}
</script>

<script setup lang="ts">
import { computed } from "vue";

import { InfoPopover, type InfoSection } from "@/presentation/common/components";

import { CellFills } from "./CellFills";
import DeploymentStatusLegend from "./DeploymentStatusLegend.vue";

/**
 * What the deployments panel's info button holds: the calendar's key while the calendar shows — what a square is and
 * how to move between days, what its colour and marks say, how today and the chosen day are told apart — and the
 * statuses a deployment's pill uses in the List view. A chosen day's own info button explains its statuses there.
 */
defineOptions({ name: "DeploymentsInfo" });
const props = defineProps<DeploymentsInfoProps>();

const CalendarSections: readonly InfoSection[] = [
  { key: "days", title: "One square per day" },
  { key: "outcomes", title: "Colour is the attention" },
  { key: "marks", title: "Today and the chosen day" },
];
const StatusSection: InfoSection = { key: "statuses", title: "States" };
const UnchangedSection: InfoSection = { key: "unchanged", title: "Deploys that changed nothing" };
const sections = computed(() => [...(props.isGrid ? CalendarSections : [StatusSection]), UnchangedSection]);
const scale = computed(() =>
  CellFills[props.status === "needs-action" ? "danger" : props.status === "look" ? "warning" : "success"].slice(1),
);

</script>

<template>
  <InfoPopover title="Deployments" :sections="sections">
    <template #days>
      <p>
        Weeks run left to right, Monday on top; finished rollouts that changed the environment count on the day they
        were submitted. Point at a day for its counts, choose it to see its deploys as one row per product, and move between
        days with the arrow keys.
      </p>
    </template>
    <template #outcomes>
      <ul class="flex flex-col gap-1.5" aria-label="Colours">
        <li class="flex items-center gap-2">
          <span class="size-3.5 shrink-0 rounded-[3px] bg-success" aria-hidden="true" />Nothing needs you
        </li>
        <li class="flex items-center gap-2">
          <span class="flex size-3.5 shrink-0 items-center justify-center rounded-[3px] bg-warning" aria-hidden="true"
            ><svg viewBox="0 0 8 8" class="size-2 text-warning-foreground/90">
              <circle cx="4" cy="4" r="3" fill="none" stroke="currentColor" stroke-width="1.2" />
              <path d="M4 1a3 3 0 0 1 0 6z" fill="currentColor" /></svg
          ></span>
          One is worth a look — degraded or rolled back; a half-dot marks it
        </li>
        <li class="flex items-center gap-2">
          <span
            class="flex size-3.5 shrink-0 items-center justify-center rounded-[3px] bg-destructive"
            aria-hidden="true"
            ><svg viewBox="0 0 8 8" class="size-2 text-destructive-foreground/90">
              <path d="M1.5 1.5l5 5M6.5 1.5l-5 5" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" /></svg
          ></span>
          One needs action — partial or failed; a cross marks it
        </li>
      </ul>
      <p class="mt-2.5">A day takes the loudest of its rollouts. A deploy that changed nothing never colours a day.</p>
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
    <template #statuses><DeploymentStatusLegend /></template>
    <template #unchanged>
      <p>
        Refused by a freeze or a gate, replaced in the queue by a newer release, cancelled, or stopped by a failed check
        before any change: they stay out of the records and the measures. The eye toggle beside Grid and List counts
        them and, pressed, shows them in the list, the calendar's days and the chosen day's rows (dashed), the product tabs and the
        measures. This browser
        remembers it.
      </p>
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
