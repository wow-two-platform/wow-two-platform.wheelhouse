<script lang="ts">
import type { DeploymentItem } from "@/domain/deployments";

/** Defines the chosen day's counts. */
export interface DeploymentDayStatsProps {
  /** The day's deployments under the filter; undefined until the first read, which draws placeholder counts. */
  readonly items: readonly DeploymentItem[] | undefined;
  /** Whether older deployments of the day remain unread, so the counts cover only those read. */
  readonly hasMore?: boolean;
}
</script>

<script setup lang="ts">
import { computed } from "vue";

import { SkeletonState } from "@wow-two-beta/fe/presentation/feedback";

import { AttentionTones, DeployState, type StatusTone } from "@/domain/common";
import { DeploymentStates, type DeploymentStateCount } from "@/domain/deployments";
import { DeployStateIcons } from "@/presentation/common/components";

/**
 * The chosen day's counts in its header: one icon and number per state that occurred — succeeded, degraded, rolled
 * back, partial, failed, in progress, not applied, unknown — the icon in the colour of the loudest attention among
 * them, its word in the title and for assistive technology; a state nobody reached takes no count. They wrap, so a
 * narrow day panel keeps every one; a trailing `+` says older deployments of the day remain unread.
 */
defineOptions({ name: "DeploymentDayStats" });
const props = withDefaults(defineProps<DeploymentDayStatsProps>(), { hasMore: false });
defineSlots<{}>();

const counts = computed(() => (props.items ? DeploymentStates.counts(props.items) : []));

/** An icon's colour by its attention's tone. @internal */
const ToneText: Readonly<Record<StatusTone, string>> = {
  calm: "text-success",
  info: "text-info",
  warning: "text-warning",
  danger: "text-destructive",
  quiet: "text-muted-foreground",
  empty: "text-subtle-foreground",
};

/** A count's icon colour: its attention's, except a calm state other than success, whose icon stays muted. @internal */
function iconClass(count: DeploymentStateCount): string {
  const tone = AttentionTones[count.attention];
  return tone === "calm" && count.state !== DeployState.Succeeded ? "text-muted-foreground" : ToneText[tone];
}
</script>

<template>
  <ul v-if="!props.items" class="flex min-w-0 flex-wrap gap-3" aria-hidden="true">
    <li v-for="index in 4" :key="index"><SkeletonState class="h-5 w-8 rounded" /></li>
  </ul>
  <ul v-else-if="counts.length" class="flex min-w-0 flex-wrap items-center gap-x-3 gap-y-1" aria-label="The day's deployments by state">
    <li
      v-for="count in counts"
      :key="count.state"
      class="inline-flex items-center gap-1 text-sm font-semibold tabular-nums"
      :title="`${count.count} ${count.word.toLowerCase()}`"
    >
      <component :is="DeployStateIcons[count.state]" :size="14" aria-hidden="true" :class="['shrink-0', iconClass(count)]" />
      {{ count.count }}<span class="sr-only"> {{ count.word.toLowerCase() }}</span>
    </li>
    <li v-if="props.hasMore" class="text-sm text-muted-foreground" title="Load older deployments to count the rest">
      +<span class="sr-only"> more not yet read</span>
    </li>
  </ul>
</template>
