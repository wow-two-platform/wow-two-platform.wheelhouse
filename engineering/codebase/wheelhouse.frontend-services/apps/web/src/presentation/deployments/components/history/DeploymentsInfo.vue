<script lang="ts">
/** Defines the deployments panel's info button. */
export interface DeploymentsInfoProps {
  /** Whether the calendar shows; the List view needs only the statuses. */
  readonly isGrid: boolean;
}
</script>

<script setup lang="ts">
import { computed } from "vue";

import { InfoPopover, type InfoSection } from "@/presentation/common/components";

import DeploymentStatusLegend from "./DeploymentStatusLegend.vue";

/**
 * What the deployments panel's info button holds, kept to a glance: while the calendar shows, four short lines — a
 * square per day, its three colours, what All attempts changes, how to choose a day and move — and in the List view,
 * the statuses a deployment's pill uses with the same attempts line. A chosen day's own info button explains its
 * statuses there.
 */
defineOptions({ name: "DeploymentsInfo" });
const props = defineProps<DeploymentsInfoProps>();

const ListSections: readonly InfoSection[] = [
  { key: "statuses", title: "States" },
  { key: "attempts", title: "Attempts" },
];
const sections = computed(() => (props.isGrid ? [] : ListSections));
</script>

<template>
  <InfoPopover title="Deployments" :sections="sections">
    <template v-if="props.isGrid" #default>
      <p>One square per day, Monday on top.</p>
      <ul class="my-2 flex flex-col gap-1.5" aria-label="Colours">
        <li class="flex items-center gap-2">
          <span class="size-3.5 shrink-0 rounded-[3px] bg-success" aria-hidden="true" />Nothing needs you
        </li>
        <li class="flex items-center gap-2">
          <span class="flex size-3.5 shrink-0 items-center justify-center rounded-[3px] bg-warning" aria-hidden="true"
            ><svg viewBox="0 0 8 8" class="size-2 text-warning-foreground/90">
              <circle cx="4" cy="4" r="3" fill="none" stroke="currentColor" stroke-width="1.2" />
              <path d="M4 1a3 3 0 0 1 0 6z" fill="currentColor" /></svg
          ></span>
          Worth a look
        </li>
        <li class="flex items-center gap-2">
          <span
            class="flex size-3.5 shrink-0 items-center justify-center rounded-[3px] bg-destructive"
            aria-hidden="true"
            ><svg viewBox="0 0 8 8" class="size-2 text-destructive-foreground/90">
              <path d="M1.5 1.5l5 5M6.5 1.5l-5 5" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" /></svg
          ></span>
          Needs action
        </li>
      </ul>
      <p>Off: each target's last deploy of the day. All attempts: every attempt.</p>
      <p class="mt-1">Choose a day for its rows; arrow keys move.</p>
    </template>
    <template #statuses><DeploymentStatusLegend /></template>
    <template #attempts>
      <p>Off: each target's last deploy of the day. All attempts: every attempt.</p>
    </template>
  </InfoPopover>
</template>
