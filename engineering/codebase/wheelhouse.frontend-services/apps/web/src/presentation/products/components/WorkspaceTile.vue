<script lang="ts">
import type { WorkspaceStats } from "@/domain/portfolio";
import type { Workspace } from "@/domain/workspaces";

/** One workspace as a card above the products. */
export interface WorkspaceTileProps {
  /** The workspace; null draws the card's placeholder, or names products without a workspace once loaded. */
  readonly workspace: Workspace | null;
  readonly stats: WorkspaceStats | undefined;
  /** The range's words for the pills' titles. */
  readonly rangeText: string;
  readonly isLoading?: boolean;
  /** The id of the group the card scrolls to, for `aria-controls`. */
  readonly groupId: string;
}
</script>
<script setup lang="ts">
import { Text } from "@wow-two-beta/fe/presentation/display";
import WorkspaceStatPills from "./WorkspaceStatPills.vue";
import WorkspaceTrend from "./WorkspaceTrend.vue";

/**
 * A workspace at a glance: name and product count, its description, then one line as on a product card — its figures
 * as pills (rollouts that succeeded over the range with their success rate folded in, products failing, degraded and
 * deploying) and, beside them, the range's rollouts as a chart. The whole card is one button that scrolls to the
 * workspace's products below; workspaces have no page of their own.
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
    <!-- The pills keep one line; the chart takes the room left, up to 7rem, and drops below only when even 3rem
         would not fit. -->
    <div class="mt-auto flex min-w-0 flex-wrap items-center justify-between gap-x-3 gap-y-2 pt-1">
      <WorkspaceStatPills :stats="props.isLoading ? undefined : props.stats" :range-text="props.rangeText" is-single-line />
      <div class="ms-auto flex min-w-12 max-w-28 flex-1 justify-end">
        <WorkspaceTrend :rollouts="props.isLoading ? null : (props.stats?.rollouts ?? null)" fill />
      </div>
    </div>
  </article>
</template>
