<script lang="ts">
/** A delete control that asks once before it acts. */
export interface DeleteConfirmProps {
  /** What the control removes, in the button's words, such as "Delete server". */
  readonly label: string;
  /** The question the confirmation asks, such as "Delete Helsinki?". */
  readonly question: string;
  /** Removes the record; the confirmation stays busy until it settles. */
  readonly onConfirm: () => Promise<unknown>;
}
</script>
<script setup lang="ts">
import { Trash2 } from "lucide-vue-next";
import { Button } from "@wow-two-beta/ui-vue/presentation/actions";
import { ConfirmPopover } from "@wow-two-beta/ui-vue/presentation/overlays";

/** Renders a quiet delete button whose confirmation the SDK popover asks. */
defineOptions({ name: "DeleteConfirm" });
const props = defineProps<DeleteConfirmProps>();
defineSlots<{}>();
</script>
<template>
  <ConfirmPopover :title="props.question" :confirm-label="props.label" tone="danger" :on-confirm="props.onConfirm">
    <Button type="button" size="sm" variant="ghost" tone="danger">
      <template #leading><Trash2 :size="14" /></template>{{ props.label }}
    </Button>
  </ConfirmPopover>
</template>
