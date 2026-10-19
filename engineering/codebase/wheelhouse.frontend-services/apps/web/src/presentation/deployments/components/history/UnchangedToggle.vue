<script lang="ts">
/** Defines the toggle that shows or hides the deploys that changed nothing. */
export interface UnchangedToggleProps {
  /** How many deploys changed nothing under the current filter and range; null while it is read. */
  readonly count: number | null;
}
</script>

<script setup lang="ts">
import { computed } from "vue";
import { Eye, EyeOff } from "lucide-vue-next";

import { Button } from "@wow-two-beta/fe/presentation/actions";
import { Tooltip } from "@wow-two-beta/fe/presentation/overlays";

import { useShowUnchanged } from "@/application/preferences";

/**
 * The viewer's one switch for the deploys that changed nothing — refused, replaced in the queue, cancelled, expired or
 * stopped by a failed check: a compact icon toggle carrying how many there are under the current filter and range.
 * Pressed, they show in the records (list, calendar day, product tabs) and count in the measures; this browser
 * remembers the choice. Hovering explains it in one line; on touch, the panel's info button does.
 */
defineOptions({ name: "UnchangedToggle" });
const props = defineProps<UnchangedToggleProps>();
defineSlots<{}>();

const shown = useShowUnchanged();
const hint = computed(() => {
  const count = props.count ?? 0;
  const what = `${count} ${count === 1 ? "deploy" : "deploys"} changed nothing (refused, cancelled, expired)`;
  return shown.value ? `${what} — shown in records and stats; hide them` : `${what} — show them in records and stats`;
});
</script>

<template>
  <Tooltip :content="hint">
    <Button
      variant="outline"
      tone="neutral"
      size="md"
      :aria-pressed="shown"
      :aria-label="hint"
      :class="['tabular-nums max-md:min-h-11', shown ? 'bg-muted' : '']"
      @click="shown = !shown"
    >
      <template #leading><component :is="shown ? Eye : EyeOff" :size="16" aria-hidden="true" /></template>
      <span aria-hidden="true">{{ props.count ?? "–" }}</span>
    </Button>
  </Tooltip>
</template>
