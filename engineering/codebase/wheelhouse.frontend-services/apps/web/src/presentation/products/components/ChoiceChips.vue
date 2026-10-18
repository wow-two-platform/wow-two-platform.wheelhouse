<script lang="ts">
/** A single choice among a few options, as a wrap of chips. */
export interface ChoiceChipsProps {
  /** Each option's value and label, in order. */
  readonly options: Readonly<Record<string, string>>;
  readonly modelValue: string;
  /** The group's accessible name. */
  readonly label: string;
}
</script>
<script setup lang="ts">
/**
 * A radio group drawn as chips that wrap: every option in view at once and each a full touch target, for a phone
 * sheet where a dropdown inside a sheet would be one tap too many. Arrow keys move between the chips.
 */
defineOptions({ name: "ChoiceChips" });
const props = defineProps<ChoiceChipsProps>();
const emit = defineEmits<{ "update:modelValue": [value: string] }>();
defineSlots<{}>();

/** Moves the choice with the arrow keys, as a radio group does. @internal */
function move(event: KeyboardEvent, index: number): void {
  const keys = Object.keys(props.options);
  const step = event.key === "ArrowRight" || event.key === "ArrowDown" ? 1 : event.key === "ArrowLeft" || event.key === "ArrowUp" ? -1 : 0;
  if (!step) return;
  event.preventDefault();
  const next = keys[(index + step + keys.length) % keys.length]!;
  emit("update:modelValue", next);
  const group = (event.currentTarget as HTMLElement).parentElement;
  (group?.children[keys.indexOf(next)] as HTMLElement | undefined)?.focus();
}
</script>
<template>
  <div role="radiogroup" :aria-label="props.label" class="flex flex-wrap gap-2">
    <button
      v-for="([value, text], index) in Object.entries(props.options)"
      :key="value"
      type="button"
      role="radio"
      :aria-checked="props.modelValue === value"
      :tabindex="props.modelValue === value ? 0 : -1"
      class="inline-flex min-h-11 items-center rounded-full border px-4 text-sm transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
      :class="
        props.modelValue === value
          ? 'border-primary/50 bg-primary-soft font-medium text-primary-soft-foreground'
          : 'border-border bg-card text-foreground hover:bg-muted/60'
      "
      @click="emit('update:modelValue', value)"
      @keydown="move($event, index)"
    >
      {{ text }}
    </button>
  </div>
</template>
