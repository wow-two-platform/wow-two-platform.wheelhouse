<script lang="ts">
import type { StatusState } from "@/domain/common";

/** Defines one status pill. */
export interface StatusPillProps {
  /** The state, from the console's status vocabulary. */
  readonly state: StatusState;
  /** The pill's text: a name such as an environment, or the state's word. */
  readonly label: string;
  /** What follows the label in a quieter weight: a version, or the state's word. */
  readonly detail?: string | null;
}
</script>
<script setup lang="ts">
import { statusClass, StatusIcons, statusIconClass } from "./StatusStyles";

/**
 * An outlined status pill: the state's icon, a label, then an optional detail. Its tone, icon and words come from the
 * console's status vocabulary, the same as an environment's pill, so a state reads alike wherever it shows.
 */
defineOptions({ name: "StatusPill" });
const props = withDefaults(defineProps<StatusPillProps>(), { detail: null });
defineSlots<{}>();
</script>
<template>
  <span
    class="inline-flex h-6 max-w-full shrink-0 items-center gap-1.5 rounded-full border ps-1.5 pe-2.5 text-xs whitespace-nowrap"
    :class="statusClass(props.state)"
  >
    <component
      :is="StatusIcons[props.state]"
      :size="14"
      aria-hidden="true"
      class="shrink-0"
      :class="statusIconClass(props.state)"
    />
    <span class="min-w-0 truncate font-medium">{{ props.label }}</span>
    <span v-if="props.detail" data-detail class="min-w-0 truncate text-muted-foreground tabular-nums">{{
      props.detail
    }}</span>
  </span>
</template>
