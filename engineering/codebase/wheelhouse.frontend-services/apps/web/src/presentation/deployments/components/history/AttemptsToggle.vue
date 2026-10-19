<script lang="ts">
/** Defines the toggle that shows every deploy attempt. */
export interface AttemptsToggleProps {
  /**
   * How many attempts a latest-of-day view hides under the current filter and range — the earlier attempts a later
   * deploy replaced that day and the deploys that changed nothing; null while it is read.
   */
  readonly hidden: number | null;
}
</script>

<script setup lang="ts">
import { computed } from "vue";
import { Eye, EyeOff } from "lucide-vue-next";

import { Button } from "@wow-two-beta/fe/presentation/actions";
import { Tooltip } from "@wow-two-beta/fe/presentation/overlays";

import { useShowAllAttempts } from "@/application/preferences";

/**
 * The viewer's one Show all attempts switch: a compact icon toggle carrying how many attempts are hidden under the
 * current filter and range. Off, the calendar, the chosen day's rows and the list show each target's latest deploy of
 * a day; pressed, every attempt and every deploy that changed nothing, and the calendar marks mixed days. The measures
 * count every attempt either way. This browser remembers the choice. Hovering explains it in one line; on touch, the
 * panel's info button does.
 */
defineOptions({ name: "AttemptsToggle" });
const props = defineProps<AttemptsToggleProps>();
defineSlots<{}>();

const shown = useShowAllAttempts();
const hint = computed(() => {
  const count = props.hidden ?? 0;
  const what = `${count} earlier or unchanged ${count === 1 ? "attempt" : "attempts"}`;
  return shown.value ? `Showing every attempt — hide the ${what}` : `${what} hidden — show every attempt`;
});
</script>

<template>
  <Tooltip :content="hint">
    <Button
      variant="outline"
      tone="neutral"
      size="md"
      :aria-pressed="shown"
      :aria-label="`Show all attempts: ${hint}`"
      :class="['tabular-nums max-md:min-h-11', shown ? 'bg-muted' : '']"
      @click="shown = !shown"
    >
      <template #leading><component :is="shown ? Eye : EyeOff" :size="16" aria-hidden="true" /></template>
      <span aria-hidden="true">{{ props.hidden ?? "–" }}</span>
    </Button>
  </Tooltip>
</template>
