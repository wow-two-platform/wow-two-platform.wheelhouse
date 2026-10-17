<script lang="ts">
/** A delete control that asks once before it acts. */
export interface DeleteConfirmProps {
  /** What the control removes, in the button's words, such as "Delete server". */
  readonly label: string;
  /** The question the confirmation asks, such as "Delete Helsinki?". */
  readonly question: string;
  /** Removes the record; the confirmation stays busy until it settles. */
  readonly onConfirm: () => Promise<unknown>;
  /**
   * The size of the row the control sits in: `md` in a dialog footer beside Cancel and Save, `sm` in a list row, where
   * it grows to the row's 44px touch target on phones. Default `md`.
   */
  readonly size?: 'sm' | 'md';
}
</script>
<script setup lang="ts">
import { Trash2 } from "lucide-vue-next";
import { Button } from "@wow-two-beta/fe/presentation/actions";
import { ConfirmPopover } from "@wow-two-beta/fe/presentation/overlays";

/** Renders a quiet delete button whose confirmation the SDK popover asks; it keeps its row's size, never a smaller one. */
defineOptions({ name: "DeleteConfirm" });
const props = withDefaults(defineProps<DeleteConfirmProps>(), { size: 'md' });
defineSlots<{}>();
</script>
<template>
  <ConfirmPopover :title="props.question" :confirm-label="props.label" tone="danger" :on-confirm="props.onConfirm">
    <Button
      type="button"
      :size="props.size"
      variant="ghost"
      tone="danger"
      :class="props.size === 'sm' ? 'max-md:min-h-11' : undefined"
    >
      <template #leading><Trash2 :size="14" /></template>{{ props.label }}
    </Button>
  </ConfirmPopover>
</template>
