<script lang="ts">
import type { WorkspaceStats } from "@/domain/portfolio";
import type { Workspace } from "@/domain/workspaces";

/** One workspace as a row above the products, from tablets up. */
export interface WorkspaceListRowProps {
  readonly workspace: Workspace | null;
  readonly stats: WorkspaceStats | undefined;
  /** The range's words for the pills' titles. */
  readonly rangeText: string;
  readonly isLoading?: boolean;
  readonly groupId: string;
}

/** The row's columns: name, products, figures, chart. */
export const WorkspaceRowColumns = "grid-cols-[minmax(0,1fr)_5.5rem_minmax(0,15rem)_8.5rem]";
</script>
<script setup lang="ts">
import { Text } from "@wow-two-beta/fe/presentation/display";
import { pluralize } from "@wow-two-beta/fe/foundation/formatters";
import WorkspaceStatPills from "./WorkspaceStatPills.vue";
import WorkspaceTrend from "./WorkspaceTrend.vue";

/**
 * A workspace in one line: name and description, product count, its figures as pills — rollouts that succeeded over
 * the range with their success rate, products failing, needing a look and deploying — and the range's chart. The whole
 * row is one button that scrolls to the workspace's products.
 */
defineOptions({ name: "WorkspaceListRow" });
const props = withDefaults(defineProps<WorkspaceListRowProps>(), { isLoading: false });
const emit = defineEmits<{ open: [] }>();
defineSlots<{}>();
</script>
<template>
  <li
    class="relative grid h-[52px] items-center gap-3 border-t border-border px-3.5 transition-colors first:border-t-0 hover:bg-muted/40 has-[button:focus-visible]:outline-2 has-[button:focus-visible]:-outline-offset-2 has-[button:focus-visible]:outline-ring"
    :class="WorkspaceRowColumns"
  >
    <div class="flex min-w-0 items-baseline gap-3">
      <Text as="span" class="shrink-0 truncate text-sm font-semibold text-inherit" skeleton-width="8rem"
        ><button
          v-if="!props.isLoading"
          type="button"
          class="outline-none after:absolute after:inset-0 after:content-['']"
          :aria-controls="props.groupId"
          @click="emit('open')"
        >
          {{ props.workspace?.name ?? "No workspace" }}
        </button></Text
      >
      <span v-if="!props.isLoading && props.workspace?.description" class="min-w-0 truncate text-[13px] text-muted-foreground max-lg:hidden">{{
        props.workspace.description
      }}</span>
    </div>
    <span class="text-xs tabular-nums text-muted-foreground"
      ><template v-if="props.stats">{{ props.stats.products }} {{ pluralize(props.stats.products, "product") }}</template></span
    >
    <WorkspaceStatPills :stats="props.isLoading ? undefined : props.stats" :range-text="props.rangeText" />
    <div class="flex justify-end"><WorkspaceTrend :rollouts="props.isLoading ? null : (props.stats?.rollouts ?? null)" fill /></div>
  </li>
</template>
