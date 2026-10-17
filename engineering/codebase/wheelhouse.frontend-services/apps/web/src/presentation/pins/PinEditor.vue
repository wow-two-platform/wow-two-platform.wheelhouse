<script lang="ts">
import type { PinnedEntry } from "@/application/pins";

/** The pins in edit mode: reorder and unpin. */
export interface PinEditorProps {
  /** Every pin in custom order, including ones whose target is gone. */
  readonly entries: readonly PinnedEntry[];
  /** Tighter rows for the sidebar: smaller controls and no product name beside a service. */
  readonly compact?: boolean;
}
</script>
<script setup lang="ts">
import { computed } from "vue";
import { ArrowDown, ArrowUp, GripVertical, X } from "lucide-vue-next";
import {
  SortableGroup,
  SortableGroupHandle,
  SortableGroupItem,
  SortableGroupMoveButton,
} from "@wow-two-beta/fe/presentation/forms";
import { usePins } from "@/application/pins";
import PinIcon from "./PinIcon.vue";

/**
 * Reorders pins by dragging a handle, by Arrow Up/Down on the handle, or with the up and down buttons; each move saves
 * the whole order at once and rolls back if the host refuses it. A pin unpins from its row.
 */
defineOptions({ name: "PinEditor" });
const props = withDefaults(defineProps<PinEditorProps>(), { compact: false });
defineSlots<{}>();
const pins = usePins();

/** @internal The row controls' look; the SDK move button carries behaviour only. Touch targets grow below `md`. */
const controlBase =
  "inline-flex shrink-0 items-center justify-center rounded-md text-muted-foreground transition-colors hover:bg-muted hover:text-foreground focus-visible:outline-2 focus-visible:outline-ring disabled:pointer-events-none disabled:opacity-30 max-md:size-11";
const controlClass = computed(() => `${controlBase} ${props.compact ? "size-6" : "size-8"}`);
</script>
<template>
  <SortableGroup aria-label="Pin order" class="gap-0.5" @reorder="(from, to) => pins.move(from, to)">
    <SortableGroupItem
      v-for="(entry, index) in props.entries"
      :key="entry.pin.id"
      :index="index"
      :class="[
        'flex min-h-9 items-center rounded-md pe-0.5 data-[over]:bg-primary-soft/50',
        props.compact ? 'gap-1' : 'gap-1.5',
      ]"
    >
      <SortableGroupHandle :class="[controlClass, 'max-md:hidden']" :aria-label="`Drag ${entry.label} to reorder`"
        ><GripVertical :size="14"
      /></SortableGroupHandle>
      <PinIcon :entry="entry" />
      <span class="min-w-0 flex-1 truncate text-sm" :class="entry.location ? '' : 'text-muted-foreground'"
        >{{ entry.label
        }}<span v-if="entry.detail && !props.compact" class="text-xs text-muted-foreground"> · {{ entry.detail }}</span></span
      >
      <SortableGroupMoveButton direction="previous" :label="`Move ${entry.label} up`" :class="controlClass"
        ><ArrowUp :size="14"
      /></SortableGroupMoveButton>
      <SortableGroupMoveButton direction="next" :label="`Move ${entry.label} down`" :class="controlClass"
        ><ArrowDown :size="14"
      /></SortableGroupMoveButton>
      <button type="button" :class="controlClass" :aria-label="`Unpin ${entry.label}`" @click="pins.unpin(entry.pin.id)">
        <X :size="14" />
      </button>
    </SortableGroupItem>
  </SortableGroup>
</template>
