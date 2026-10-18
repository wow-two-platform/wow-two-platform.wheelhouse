<script lang="ts">
import type { WorkspaceStats } from "@/domain/portfolio";
import type { Workspace } from "@/domain/workspaces";

/** One workspace as a row above the products, from tablets up. */
export interface WorkspaceListRowProps {
  readonly workspace: Workspace | null;
  readonly stats: WorkspaceStats | undefined;
  readonly weeks: readonly string[];
  readonly isLoading?: boolean;
  readonly groupId: string;
}

/** The row's columns: name, products, needs you, success, trend — shared with the list's header. */
export const WorkspaceRowColumns = "grid-cols-[minmax(0,1fr)_5.5rem_minmax(0,14rem)_6rem_4.75rem]";
</script>
<script setup lang="ts">
import { Text } from "@wow-two-beta/fe/presentation/display";
import WorkspaceRollup from "./WorkspaceRollup.vue";
import WorkspaceTrend from "./WorkspaceTrend.vue";

/** A workspace in one line: name and description, product count, what needs the owner, success rate and trend. The
 * whole row is one button that scrolls to the workspace's products. */
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
      ><template v-if="props.stats">{{ props.stats.products }} product{{ props.stats.products === 1 ? "" : "s" }}</template></span
    >
    <WorkspaceRollup :stats="props.isLoading ? undefined : props.stats" is-compact />
    <span class="text-xs text-muted-foreground tabular-nums"
      ><template v-if="props.stats?.successRate != null"
        ><span class="font-semibold text-foreground">{{ props.stats.successRate }}%</span> success</template
      ><template v-else-if="props.stats">—</template></span
    >
    <div class="flex justify-end"><WorkspaceTrend :stats="props.isLoading ? undefined : props.stats" :weeks="props.weeks" /></div>
  </li>
</template>
