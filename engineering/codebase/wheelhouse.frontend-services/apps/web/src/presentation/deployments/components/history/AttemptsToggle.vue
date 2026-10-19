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
 * The viewer's one All attempts switch, in the deployments filter bar beside the products and the range: its name and a
 * badge with how many attempts are hidden under the current filter and range. Off, the calendar, the chosen day's rows,
 * the list and the measures take each target's latest deploy of a day; pressed, every attempt and every deploy that
 * changed nothing, and the calendar marks mixed days. This browser remembers the choice. Hovering explains it in one
 * line; on touch, the filter's info button does. On a phone the name folds away, leaving the icon and the badge, so the
 * toggle and the range keep one line; the accessible name carries the whole hint.
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
      :class="['shrink-0 max-md:min-h-11', shown ? 'bg-muted' : '']"
      @click="shown = !shown"
    >
      <template #leading><component :is="shown ? Eye : EyeOff" :size="16" aria-hidden="true" /></template>
      <span aria-hidden="true" class="max-md:hidden">All attempts</span>
      <span
        aria-hidden="true"
        :class="[
          'ms-0.5 inline-flex h-5 min-w-5 items-center justify-center rounded-full px-1.5 text-xs font-medium tabular-nums',
          shown ? 'bg-background text-foreground' : 'bg-muted text-muted-foreground',
        ]"
        >{{ props.hidden ?? "–" }}</span
      >
    </Button>
  </Tooltip>
</template>
