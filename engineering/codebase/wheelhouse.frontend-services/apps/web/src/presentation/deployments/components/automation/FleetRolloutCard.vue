<script lang="ts">
import type { FleetRollout } from "@/domain/deployments";

/** Defines one fleet rollout's card. */
export interface FleetRolloutCardProps {
  readonly fleet: FleetRollout;
  /** Product names by slug. */
  readonly productNames: ReadonlyMap<string, string>;
  /** Whether a cancel request is on its way. */
  readonly cancelling?: boolean;
}
</script>

<script setup lang="ts">
import { computed } from "vue";
import { RouterLink } from "vue-router";
import { ArrowRight, Ban, CircleCheck, CircleDashed, CircleX, Clock, LoaderCircle } from "lucide-vue-next";

import { Button } from "@wow-two-beta/fe/presentation/actions";
import { Badge, RelativeTime } from "@wow-two-beta/fe/presentation/display";

import { ConsolePaths } from "@/application/navigation";
import { FleetItemStatus, FleetRollouts, FleetStatus, type FleetRolloutItem } from "@/domain/deployments";

/**
 * One fleet rollout: how far it got, who started it and why, then each product in order with its outcome and a link to
 * its deployment. A running fleet offers Cancel, which stops it after the product it is on.
 */
defineOptions({ name: "FleetRolloutCard" });
const props = withDefaults(defineProps<FleetRolloutCardProps>(), { cancelling: false });
const emit = defineEmits<{ cancel: [id: string] }>();

const progress = computed(() => FleetRollouts.progress(props.fleet));
const tone = computed(
  () =>
    ({
      [FleetStatus.Running]: "info",
      [FleetStatus.Succeeded]: "success",
      [FleetStatus.Failed]: "danger",
      [FleetStatus.Cancelled]: "neutral",
    })[props.fleet.status] as "info" | "success" | "danger" | "neutral",
);
const statusWord = computed(
  () => ({ running: "Running", succeeded: "Promoted", failed: "Stopped", cancelled: "Cancelled" })[props.fleet.status],
);

function itemTone(item: FleetRolloutItem): string {
  return item.status === FleetItemStatus.Succeeded
    ? "text-success"
    : item.status === FleetItemStatus.Failed || item.status === FleetItemStatus.Refused
      ? "text-destructive"
      : item.status === FleetItemStatus.Running
        ? "text-primary"
        : "text-muted-foreground";
}
</script>

<template>
  <section class="flex flex-col gap-3" :aria-label="`Fleet rollout of ${progress.total} products`">
    <div class="flex flex-wrap items-center justify-between gap-2">
      <div class="flex min-w-0 flex-col gap-1">
        <p class="flex flex-wrap items-center gap-2">
          <Badge :variant="tone">{{ statusWord }}</Badge>
          <span class="font-medium">{{ FleetRollouts.summary(props.fleet) }}</span>
        </p>
        <p class="text-sm text-muted-foreground">
          Fleet rollout by {{ props.fleet.actor }} · started <RelativeTime :value="props.fleet.createdAt" />
          <template v-if="props.fleet.reason"> · {{ props.fleet.reason }}</template>
          <template v-if="props.fleet.overrideFreeze"> · overriding freezes</template>
        </p>
      </div>
      <Button
        v-if="FleetRollouts.canCancel(props.fleet)"
        size="sm"
        variant="outline"
        tone="danger"
        class="max-md:min-h-11"
        :is-loading="props.cancelling"
        @click="emit('cancel', props.fleet.id)"
      >
        <template #leading><Ban :size="14" aria-hidden="true" /></template>Cancel after this product
      </Button>
    </div>
    <div
      v-if="props.fleet.status === FleetStatus.Running"
      class="h-1.5 overflow-hidden rounded-full bg-muted"
      role="progressbar"
      :aria-valuenow="progress.done"
      :aria-valuemin="0"
      :aria-valuemax="progress.total"
      :aria-label="`${progress.done} of ${progress.total} products done`"
    >
      <div class="h-full bg-primary transition-[width]" :style="{ width: `${(progress.done / Math.max(1, progress.total)) * 100}%` }" />
    </div>
    <ol class="flex flex-col gap-2">
      <li v-for="item in props.fleet.items" :key="item.position" class="grid grid-cols-[1.25rem_minmax(0,1fr)] items-start gap-x-3 gap-y-0.5">
        <span :class="itemTone(item)" class="pt-0.5">
          <LoaderCircle v-if="item.status === FleetItemStatus.Running" :size="16" class="motion-safe:animate-spin" aria-hidden="true" />
          <CircleCheck v-else-if="item.status === FleetItemStatus.Succeeded" :size="16" aria-hidden="true" />
          <CircleX v-else-if="item.status === FleetItemStatus.Failed || item.status === FleetItemStatus.Refused" :size="16" aria-hidden="true" />
          <Clock v-else-if="item.status === FleetItemStatus.Waiting" :size="16" aria-hidden="true" />
          <CircleDashed v-else :size="16" aria-hidden="true" />
        </span>
        <span class="min-w-0">
          <span class="flex flex-wrap items-center gap-x-2 gap-y-1 text-sm">
            <RouterLink :to="ConsolePaths.product(item.product)" class="font-medium hover:text-primary focus-visible:outline-primary">{{
              props.productNames.get(item.product) ?? item.product
            }}</RouterLink>
            <span class="font-mono text-xs text-muted-foreground">{{ item.fromRelease ?? "nothing" }}</span>
            <ArrowRight :size="12" aria-label="to" />
            <span class="font-mono text-xs">{{ item.release ?? item.bundleId }}</span>
            <span :class="itemTone(item)" class="text-xs">{{ FleetRollouts.itemLabel(item.status) }}</span>
            <RouterLink
              v-if="item.deploymentId"
              :to="`${ConsolePaths.deployments}/${encodeURIComponent(item.deploymentId)}`"
              class="text-xs text-primary hover:underline"
              >deployment</RouterLink
            >
          </span>
          <span v-if="item.detail" class="block text-xs break-words text-muted-foreground">{{ item.detail }}</span>
        </span>
      </li>
    </ol>
  </section>
</template>
