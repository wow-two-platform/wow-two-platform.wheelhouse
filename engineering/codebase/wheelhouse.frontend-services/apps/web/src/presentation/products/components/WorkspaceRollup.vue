<script lang="ts">
import type { WorkspaceStats } from "@/domain/portfolio";

/** A workspace's problem counts. */
export interface WorkspaceRollupProps {
  readonly stats: WorkspaceStats | undefined;
  /** Numbers with icons only, for a narrow column; the words move into each item's title. */
  readonly isCompact?: boolean;
}
</script>
<script setup lang="ts">
import { computed } from "vue";
import { CircleX, LoaderCircle, TriangleAlert } from "lucide-vue-next";
import { SkeletonState } from "@wow-two-beta/fe/presentation/feedback";

/**
 * What needs the owner across a workspace's products: failing, needing a look, and deploying, each only when it has
 * any, with an icon and a word so colour is never alone; a calm workspace says so in muted text.
 */
defineOptions({ name: "WorkspaceRollup" });
const props = withDefaults(defineProps<WorkspaceRollupProps>(), { isCompact: false });
defineSlots<{}>();
const items = computed(() => {
  const stats = props.stats;
  if (!stats) return [];
  return [
    { key: "failing", count: stats.failing, word: "failing", icon: CircleX, tone: "text-destructive" },
    {
      key: "attention",
      count: stats.attention,
      word: stats.attention === 1 ? "needs a look" : "need a look",
      icon: TriangleAlert,
      tone: "text-warning-soft-foreground",
    },
    { key: "deploying", count: stats.deploying, word: "deploying", icon: LoaderCircle, tone: "text-info-soft-foreground" },
  ].filter((item) => item.count > 0);
});
</script>
<template>
  <SkeletonState v-if="!props.stats" class="h-4 w-28 rounded" />
  <ul v-else-if="items.length" role="list" class="flex min-w-0 flex-wrap items-center gap-x-3 gap-y-1 text-xs">
    <li
      v-for="item in items"
      :key="item.key"
      class="inline-flex items-center gap-1 font-medium tabular-nums"
      :class="item.tone"
      :title="`${item.count} ${item.word}`"
    >
      <component :is="item.icon" :size="13" aria-hidden="true" />{{ item.count
      }}<span :class="props.isCompact ? 'sr-only' : ''">&nbsp;{{ item.word }}</span>
    </li>
  </ul>
  <p v-else class="text-xs text-muted-foreground">
    {{ props.stats.products ? "Nothing needs you" : "No products yet" }}
  </p>
</template>
