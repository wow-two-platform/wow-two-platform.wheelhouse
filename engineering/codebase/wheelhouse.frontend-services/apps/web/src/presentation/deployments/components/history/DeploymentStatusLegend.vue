<script setup lang="ts">
import { Attention, DeployState } from "@/domain/common";
import { DeploymentStates } from "@/domain/deployments";

import DeploymentStateChip from "../DeploymentStateChip.vue";

/** Each state a deployment can be in, at its usual attention, and what the environment runs. @internal */
const States = [
  { state: DeployState.InProgress, attention: Attention.Info, text: "The environment is changing: queued, checking, starting, rolling back." },
  { state: DeployState.Succeeded, attention: Attention.None, text: "The new release runs, healthy." },
  { state: DeployState.Degraded, attention: Attention.Look, text: "The new release runs, not all of it healthy: at the deploy, or regressed while watched." },
  { state: DeployState.RolledBack, attention: Attention.Look, text: "The previous release runs again, whole." },
  { state: DeployState.Partial, attention: Attention.Act, text: "A mix of releases runs; reconcile it. Amber when every service is healthy." },
  { state: DeployState.Failed, attention: Attention.Act, text: "Broken or unknown: the roll back failed or was not allowed, or it stopped mid-way." },
  { state: DeployState.NotApplied, attention: Attention.None, text: "Unchanged: refused, replaced, cancelled. Amber when a check failed, a schedule expired or it gave up." },
  { state: DeployState.Unknown, attention: Attention.Muted, text: "No receipt from the target." },
].map((item) => ({ ...item, reading: DeploymentStates.of({ status: "unknown", state: item.state, attention: item.attention }) }));

/**
 * The deployment states as their chips, each with what the environment runs: the legend behind the console's info
 * buttons. The icon says what happened, the colour how much attention it needs, the word both.
 */
defineOptions({ name: "DeploymentStatusLegend" });
defineSlots<{}>();
</script>

<template>
  <div class="flex flex-col gap-2.5">
    <p class="text-xs text-muted-foreground">
      The icon says what happened, the colour how much attention it needs. After the word, the way it got there; under
      the chip, where the trouble started and why.
    </p>
    <ul class="flex flex-col gap-2" aria-label="States">
      <li v-for="item in States" :key="item.state" class="flex items-start gap-2.5">
        <span class="w-28 shrink-0"><DeploymentStateChip :reading="item.reading" /></span
        ><span class="pt-0.5 text-xs text-muted-foreground">{{ item.text }}</span>
      </li>
    </ul>
  </div>
</template>
