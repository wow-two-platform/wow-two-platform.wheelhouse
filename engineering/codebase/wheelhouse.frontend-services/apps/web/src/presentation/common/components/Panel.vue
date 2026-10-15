<script lang="ts">
/** Defines a titled operational region in the Wheelhouse workspace. */
export interface PanelProps {
  readonly title: string;
  readonly description?: string;
}
</script>
<script setup lang="ts">
import { Card, SectionHeading } from '@wow-two-beta/ui-vue/presentation/display';

/**
 * Groups related operational data and its local actions. The SDK heading's smallest step is `text-lg`, so the
 * panel keeps its `text-base` title with one descendant class.
 */
defineOptions({ name: 'Panel' });
const props = defineProps<PanelProps>();
defineSlots<{ default(): unknown; actions?(): unknown; title?(): unknown }>();
</script>
<template>
  <Card class="wh-glass min-w-0 overflow-hidden rounded-2xl border border-(--wh-glass-edge) bg-(--wh-glass-fill) shadow-(--wh-glass-shadow)">
    <SectionHeading
      :title="props.title"
      v-bind="props.description ? { description: props.description } : {}"
      size="md"
      class="wh-glass-subtle flex-wrap items-center gap-3 px-5 py-4 [&_h2]:text-base"
    >
      <template v-if="$slots.title" #title><slot name="title" /></template>
      <template v-if="$slots.actions" #actions><slot name="actions" /></template>
    </SectionHeading>
    <div class="min-w-0 bg-card p-5"><slot /></div>
  </Card>
</template>
