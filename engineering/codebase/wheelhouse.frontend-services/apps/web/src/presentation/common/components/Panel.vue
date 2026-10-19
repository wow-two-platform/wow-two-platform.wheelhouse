<script lang="ts">
/** Defines a titled operational region in the Wheelhouse workspace. */
export interface PanelProps {
  readonly title: string;
  /** One line under the title, only when it changes what the reader does next; context goes behind the info button. */
  readonly description?: string;
}
</script>
<script setup lang="ts">
import { Card, Heading, Text } from '@wow-two-beta/fe/presentation/display';

/**
 * Groups related operational data and its local actions. The title row holds the heading and, after it, the panel's
 * info button (`info` slot) — outside the heading, so the heading's name stays the title. The panel is the info
 * button's region (`data-info-scope`): on a pointer device the button shows while the pointer or focus is inside.
 */
defineOptions({ name: 'Panel' });
const props = defineProps<PanelProps>();
defineSlots<{ default(): unknown; actions?(): unknown; title?(): unknown; info?(): unknown }>();
</script>
<template>
  <Card
    data-info-scope
    class="wh-glass min-w-0 overflow-hidden rounded-2xl border border-(--wh-glass-edge) bg-(--wh-glass-fill) shadow-(--wh-glass-shadow)"
  >
    <header
      class="wh-glass-subtle flex flex-wrap items-center justify-between gap-3 border-b border-border px-5 py-4 [&_h2]:text-base"
    >
      <div class="flex min-w-0 flex-col gap-1">
        <div class="flex min-w-0 items-center gap-1">
          <Heading :level="2" size="md"
            ><slot name="title">{{ props.title }}</slot></Heading
          >
          <slot name="info" />
        </div>
        <Text v-if="props.description" size="sm" color="muted">{{ props.description }}</Text>
      </div>
      <div v-if="$slots.actions" class="flex shrink-0 items-center gap-2"><slot name="actions" /></div>
    </header>
    <div class="min-w-0 bg-card p-5"><slot /></div>
  </Card>
</template>
