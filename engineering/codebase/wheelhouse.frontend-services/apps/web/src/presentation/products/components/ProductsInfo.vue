<script setup lang="ts">
import { PillState, PillStateWords, type EnvironmentPill as Pill } from "@/domain/portfolio";
import { InfoPopover, type InfoSection } from "@/presentation/common/components";
import EnvironmentPill from "./EnvironmentPill.vue";

/**
 * How to read the Products page, behind an info button: what each environment pill says, what a deploy trend shows,
 * and what a workspace card counts.
 */
defineOptions({ name: "ProductsInfo" });
defineSlots<{}>();

const sections: InfoSection[] = [
  { key: "pills", title: "One pill per environment" },
  { key: "trend", title: "Deploy trend" },
  { key: "workspaces", title: "Workspace cards" },
];

/** A sample pill of each state, for the legend. @internal */
const sample = (state: Pill["state"], environment: string, detail: string | null): Pill => ({
  environment, state, detail, cause: null, release: null, lastDeploy: null, running: null, hasTarget: state !== PillState.None,
});
const legend: { pill: Pill; text: string }[] = [
  { pill: sample(PillState.Healthy, "prod", "1.2.0"), text: "Serving that version; only the check is green." },
  { pill: sample(PillState.Deploying, "dev", PillStateWords.deploying), text: "A rollout is running." },
  { pill: sample(PillState.Failed, "prod", PillStateWords.failed), text: "Unreachable, or its latest rollout failed." },
  {
    pill: sample(PillState.Degraded, "prod", PillStateWords.degraded),
    text: "Serving, but rolled back to the previous release, or restarting or under host pressure; its details name the cause.",
  },
  { pill: sample(PillState.None, "prod", null), text: "No target, or nothing deployed yet." },
  { pill: sample(PillState.Ci, "prod", "0.4.3"), text: "Deployed by its own CI, as Wheelhouse is." },
];
</script>
<template>
  <InfoPopover title="Products" :sections="sections">
    <template #pills>
      <p>Point at a pill for the release serving and the latest rollout: when, by whom and why it failed.</p>
      <ul class="mt-2.5 flex flex-col gap-2" aria-label="Pill states">
        <li v-for="item in legend" :key="item.pill.state" class="flex items-start gap-2.5">
          <span class="w-36 shrink-0"><EnvironmentPill :pill="item.pill" /></span
          ><span class="pt-0.5 text-xs text-muted-foreground">{{ item.text }}</span>
        </li>
      </ul>
    </template>
    <template #trend>
      <p>
        Finished rollouts per week over twelve weeks:
        <span class="inline-flex items-center gap-1"
          ><span class="size-2.5 rounded-sm bg-(--color-chart-prod)" aria-hidden="true" />prod</span
        >
        from the baseline,
        <span class="inline-flex items-center gap-1"
          ><span class="size-2.5 rounded-sm bg-(--color-chart-dev)" aria-hidden="true" />dev</span
        >
        above it. Every product shares one scale, so a busy one stands taller. Point at a week for its counts.
      </p>
    </template>
    <template #workspaces>
      <p>
        Each workspace counts its failing products and those that need a look, and its rollouts' success rate over the
        same weeks, failures red in its trend. Choose one to jump to its products.
      </p>
    </template>
  </InfoPopover>
</template>
