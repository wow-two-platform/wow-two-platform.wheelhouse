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
import { computed } from "vue";
import { Badge } from "@wow-two-beta/fe/presentation/display";
import { StatusIcons, isSpinningState, statusBadge } from "./StatusStyles";

/**
 * The console's status pill: the SDK outlined badge with the state's icon, a label, then an optional detail. Its tone,
 * icon and words come from the console's status vocabulary, the same as an environment's pill, so a state reads alike
 * wherever it shows.
 */
defineOptions({ name: "StatusPill" });
const props = withDefaults(defineProps<StatusPillProps>(), { detail: null });
defineSlots<{}>();

const look = computed(() => statusBadge(props.state));
</script>
<template>
  <Badge
    :variant="look.variant"
    is-outlined
    :border-style="look.borderStyle"
    v-bind="props.detail ? { detail: props.detail } : {}"
    :is-spinning="isSpinningState(props.state)"
    class="shrink-0"
    :class="look.className"
  >
    <template #leading><component :is="StatusIcons[props.state]" :size="14" :class="look.iconClass" /></template>
    {{ props.label }}
  </Badge>
</template>
