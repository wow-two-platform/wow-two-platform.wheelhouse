<script lang="ts">
/** A delete control that asks once before it acts. */
export interface DeleteConfirmProps {
  /** What the control removes, in the button's words, such as "Delete server". */
  readonly label: string;
  readonly isLoading?: boolean;
}
</script>
<script setup lang="ts">
import { ref } from "vue";
import { Trash2 } from "lucide-vue-next";
import { Button } from "@wow-two-beta/ui-vue/presentation/actions";

/** Renders a quiet delete button that turns into a confirm and cancel pair. */
defineOptions({ name: "DeleteConfirm" });
const props = withDefaults(defineProps<DeleteConfirmProps>(), { isLoading: false });
const emit = defineEmits<{ confirm: [] }>();
defineSlots<{}>();
const asking = ref(false);
</script>
<template>
  <span v-if="asking" class="inline-flex gap-2">
    <Button type="button" size="sm" variant="ghost" tone="neutral" :is-disabled="props.isLoading" @click="asking = false">Keep</Button>
    <Button type="button" size="sm" tone="danger" :is-loading="props.isLoading" @click="emit('confirm')">{{ props.label }}</Button>
  </span>
  <Button v-else type="button" size="sm" variant="ghost" tone="danger" @click="asking = true">
    <template #leading><Trash2 :size="14" /></template>{{ props.label }}
  </Button>
</template>
