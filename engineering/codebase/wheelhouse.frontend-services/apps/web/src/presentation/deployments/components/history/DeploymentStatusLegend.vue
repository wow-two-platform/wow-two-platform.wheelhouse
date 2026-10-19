<script setup lang="ts">
import { StatusState, StatusWords } from "@/domain/common";
import { StatusPill } from "@/presentation/common/components";

/** Each status a deployment can end in, and what it means. @internal */
const Statuses: ReadonlyArray<{ readonly state: StatusState; readonly text: string }> = [
  { state: StatusState.Succeeded, text: "Finished as intended." },
  { state: StatusState.Failed, text: "Failed, or restoring the previous release failed too." },
  { state: StatusState.Degraded, text: "Rolled back to the previous release, or regressed while watched; the row names the cause." },
  { state: StatusState.Interrupted, text: "Stopped mid-way, then reconciled." },
  { state: StatusState.Deploying, text: "Submitted, queued or running." },
  { state: StatusState.Refused, text: "Refused by the runner before it changed anything." },
];

/** The deployment statuses as their pills, each with what it means: the legend behind the console's info buttons. */
defineOptions({ name: "DeploymentStatusLegend" });
defineSlots<{}>();
</script>

<template>
  <ul class="flex flex-col gap-2" aria-label="Statuses">
    <li v-for="item in Statuses" :key="item.state" class="flex items-start gap-2.5">
      <span class="w-28 shrink-0"><StatusPill :state="item.state" :label="StatusWords[item.state]" /></span
      ><span class="pt-0.5 text-xs text-muted-foreground">{{ item.text }}</span>
    </li>
  </ul>
</template>
