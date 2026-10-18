<script lang="ts">
import type { WorkspaceStats } from "@/domain/portfolio";
import type { Workspace } from "@/domain/workspaces";

/** One workspace as a card above the products. */
export interface WorkspaceTileProps {
  /** The workspace; null draws the card's placeholder, or names products without a workspace once loaded. */
  readonly workspace: Workspace | null;
  readonly stats: WorkspaceStats | undefined;
  readonly weeks: readonly string[];
  readonly isLoading?: boolean;
  /** The id of the group the card scrolls to, for `aria-controls`. */
  readonly groupId: string;
}
</script>
<script setup lang="ts">
import { Text } from "@wow-two-beta/fe/presentation/display";
import WorkspaceRollup from "./WorkspaceRollup.vue";
import WorkspaceTrend from "./WorkspaceTrend.vue";

/**
 * A workspace at a glance: name and product count, its description, what needs the owner across its products, and
 * its rollouts' success rate beside their weekly trend. The whole card is one button that scrolls to the workspace's
 * products below; workspaces have no page of their own.
 */
defineOptions({ name: "WorkspaceTile" });
const props = withDefaults(defineProps<WorkspaceTileProps>(), { isLoading: false });
const emit = defineEmits<{ open: [] }>();
defineSlots<{}>();
</script>
<template>
  <article
    class="group relative flex min-w-0 flex-col gap-2 rounded-2xl border border-border bg-card p-4 transition-[border-color,box-shadow] duration-150 hover:border-primary/40 hover:shadow-sm has-[button:focus-visible]:outline-2 has-[button:focus-visible]:outline-offset-2 has-[button:focus-visible]:outline-ring motion-reduce:transition-none"
  >
    <div class="flex min-w-0 items-baseline gap-2">
      <Text as="h3" class="min-w-0 flex-1 truncate text-[15px] font-semibold text-inherit" skeleton-width="8rem"
        ><button
          v-if="!props.isLoading"
          type="button"
          class="outline-none after:absolute after:inset-0 after:rounded-2xl after:content-['']"
          :aria-controls="props.groupId"
          :title="`Show the products in ${props.workspace?.name ?? 'no workspace'}`"
          @click="emit('open')"
        >
          {{ props.workspace?.name ?? "No workspace" }}
        </button></Text
      >
      <span v-if="props.stats" class="shrink-0 text-xs tabular-nums text-muted-foreground"
        >{{ props.stats.products }} product{{ props.stats.products === 1 ? "" : "s" }}</span
      >
    </div>
    <Text class="line-clamp-2 min-h-[2lh] text-[13px] leading-snug text-muted-foreground" skeleton-width="12rem">{{
      props.isLoading ? "" : (props.workspace?.description ?? "Products the console holds without a workspace.")
    }}</Text>
    <WorkspaceRollup :stats="props.isLoading ? undefined : props.stats" />
    <div class="mt-auto flex items-end justify-between gap-3 pt-1">
      <p class="text-xs text-muted-foreground">
        <template v-if="props.stats?.successRate != null"
          ><span class="text-sm font-semibold text-foreground tabular-nums">{{ props.stats.successRate }}%</span>
          success · {{ props.weeks.length }} wk</template
        >
        <template v-else-if="props.stats && props.weeks.length">No rollouts in {{ props.weeks.length }} weeks</template>
      </p>
      <WorkspaceTrend :stats="props.isLoading ? undefined : props.stats" :weeks="props.weeks" />
    </div>
  </article>
</template>
