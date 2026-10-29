<script lang="ts">
import type { WorkspaceStats } from "@/domain/portfolio";

/** A workspace's figures as compact icon pills. */
export interface WorkspaceStatPillsProps {
  readonly stats: WorkspaceStats | undefined;
  /** The range's words for the titles, such as `the last 90 days`. */
  readonly rangeText: string;
  /** Keeps the pills on one line at their own width; the row around them makes room. */
  readonly isSingleLine?: boolean;
}
</script>
<script setup lang="ts">
import { computed } from "vue";
import { pluralize } from "@wow-two-beta/fe/foundation/formatters";
import { SkeletonState } from "@wow-two-beta/fe/presentation/feedback";
import { StatusState, StatusWords } from "@/domain/common/StatusVocabulary";
import { ProductConditionWords } from "@/domain/portfolio";
import { statusClass, StatusIcons, statusIconClass } from "@/presentation/common/components/StatusStyles";

/** One pill. @internal */
interface StatPill {
  readonly state: StatusState;
  readonly text: string;
  readonly title: string;
}

/**
 * The workspace in up to four pills, each the status vocabulary's icon and a number, a zero left out: its rollouts
 * that succeeded over the range with their success rate folded in, then its products failing, degraded and deploying
 * now. Each pill's title spells out what it counts, for pointers and assistive technology; a calm workspace shows the
 * success pill alone.
 */
defineOptions({ name: "WorkspaceStatPills" });
const props = withDefaults(defineProps<WorkspaceStatPillsProps>(), { isSingleLine: false });
defineSlots<{}>();

const pills = computed<StatPill[]>(() => {
  const stats = props.stats;
  if (!stats) return [];
  const result: StatPill[] = [];
  const rollouts = stats.rollouts;
  if (rollouts && rollouts.successRate !== null) {
    const finished = rollouts.succeeded + rollouts.failed;
    result.push({
      state: StatusState.Succeeded,
      text: rollouts.succeeded ? `${rollouts.succeeded} · ${rollouts.successRate}%` : `${rollouts.successRate}%`,
      title: `${rollouts.succeeded} of ${finished} ${pluralize(finished, "rollout")} ${StatusWords.succeeded} in ${props.rangeText}: ${rollouts.successRate}%`,
    });
  }
  const now = (state: StatusState, count: number, word: string) => {
    if (count > 0) result.push({ state, text: String(count), title: `${count} ${pluralize(count, "product")} ${word}` });
  };
  now(StatusState.Failed, stats.failing, ProductConditionWords.failing);
  now(StatusState.Degraded, stats.degraded, ProductConditionWords.attention);
  now(StatusState.Deploying, stats.deploying, ProductConditionWords.deploying);
  return result;
});
const isReading = computed(() => props.stats !== undefined && props.stats.products > 0 && props.stats.rollouts === null);
</script>
<template>
  <div v-if="!props.stats || isReading" class="flex items-center gap-1.5" aria-hidden="true">
    <SkeletonState class="h-6 w-16 rounded-full" /><SkeletonState class="h-6 w-9 rounded-full" />
  </div>
  <ul
    v-else-if="pills.length"
    role="list"
    class="flex items-center gap-1.5"
    :class="props.isSingleLine ? 'flex-none' : 'min-w-0 flex-wrap'"
  >
    <li
      v-for="pill in pills"
      :key="pill.state"
      class="inline-flex h-6 shrink-0 items-center gap-1 rounded-full border ps-1.5 pe-2 text-xs font-medium tabular-nums whitespace-nowrap"
      :class="statusClass(pill.state)"
      :title="pill.title"
    >
      <component
        :is="StatusIcons[pill.state]"
        :size="13"
        class="shrink-0"
        :class="statusIconClass(pill.state)"
        aria-hidden="true"
      /><span aria-hidden="true">{{ pill.text }}</span><span class="sr-only">{{ pill.title }}</span>
    </li>
  </ul>
  <p v-else class="text-xs text-muted-foreground">
    {{ props.stats.products ? `No rollouts in ${props.rangeText}` : "No products yet" }}
  </p>
</template>
