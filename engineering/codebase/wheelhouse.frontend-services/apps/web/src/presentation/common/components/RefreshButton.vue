<script lang="ts">
/** The one refresh control every region uses. */
export interface RefreshButtonProps {
  /** True from the click until the refresh settles; the icon spins in place and the button dims. */
  readonly refreshing: boolean;
  /** The visible label; empty renders an icon-only button named by `label`. Default `Refresh`. */
  readonly label?: string;
  /** Renders the icon alone, named by `label` for assistive technology. */
  readonly iconOnly?: boolean;
  readonly size?: "sm" | "md";
  /** `outline` for a page action, `ghost` inside a panel header. Default `outline`. */
  readonly variant?: "outline" | "ghost";
}
</script>
<script setup lang="ts">
import { RefreshCw } from "lucide-vue-next";
import { Button } from "@wow-two-beta/fe/presentation/actions";

/**
 * Swaps the refresh icon for the SDK spinner and dims while running, keeping the label and the width (`Button`
 * `isLoading`). The button stays focused and swallows repeat clicks until the shared refresh (`useRefresh`) settles.
 */
defineOptions({ name: "RefreshButton" });
const props = withDefaults(defineProps<RefreshButtonProps>(), {
  label: "Refresh",
  iconOnly: false,
  size: "sm",
  variant: "outline",
});
const emit = defineEmits<{ refresh: [] }>();
</script>
<template>
  <Button
    :variant="props.variant"
    tone="neutral"
    :size="props.size"
    :is-loading="props.refreshing"
    :aria-label="props.iconOnly ? props.label : undefined"
    :title="props.iconOnly ? props.label : undefined"
    @click="emit('refresh')"
  >
    <template #leading><RefreshCw :size="14" /></template>
    <template v-if="!props.iconOnly">{{ props.label }}</template>
  </Button>
</template>
