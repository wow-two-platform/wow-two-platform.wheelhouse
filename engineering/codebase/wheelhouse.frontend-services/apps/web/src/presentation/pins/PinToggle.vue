<script lang="ts">
import type { PinSubject } from "@/domain/pins";

/** Pins or unpins one workspace, product or service. */
export interface PinToggleProps {
  readonly subject: PinSubject;
  /** What the pin names, for the button's accessible name: "Pin {label}". */
  readonly label: string;
  /** Shows "Pin" or "Pinned" beside the icon. */
  readonly showText?: boolean;
}
</script>
<script setup lang="ts">
import { computed } from "vue";
import { Pin, PinOff } from "lucide-vue-next";
import { Button } from "@wow-two-beta/fe/presentation/actions";
import { usePins } from "@/application/pins";

/** A pin toggle: pressed while pinned. The change shows at once and rolls back if the host refuses it. */
defineOptions({ name: "PinToggle" });
const props = withDefaults(defineProps<PinToggleProps>(), { showText: false });
defineSlots<{}>();
const pins = usePins();
const pinned = computed(() => pins.isPinned(props.subject));
const name = computed(() => `${pinned.value ? "Unpin" : "Pin"} ${props.label}`);
</script>
<template>
  <Button
    size="sm"
    :variant="props.showText ? 'outline' : 'ghost'"
    tone="neutral"
    :shape="props.showText ? 'default' : 'square'"
    :class="props.showText ? '' : 'max-md:size-11'"
    :aria-pressed="pinned"
    :aria-label="props.showText ? undefined : name"
    :title="name"
    @click="pins.toggle(props.subject)"
  >
    <template v-if="props.showText" #leading
      ><PinOff v-if="pinned" :size="14" /><Pin v-else :size="14"
    /></template>
    <template v-if="props.showText">{{ pinned ? "Pinned" : "Pin" }}</template>
    <template v-else
      ><Pin v-if="pinned" :size="16" class="fill-current text-primary" /><Pin v-else :size="16"
    /></template>
  </Button>
</template>
