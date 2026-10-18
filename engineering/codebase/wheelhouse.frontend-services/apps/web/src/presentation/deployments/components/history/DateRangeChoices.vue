<script lang="ts">
import type { DeploymentRange } from "@/domain/deployments";

/** Defines the range choices: presets and a custom from and to. */
export interface DateRangeChoicesProps {
  /** The range in force. */
  readonly modelValue: DeploymentRange;
}
</script>

<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { Check } from "lucide-vue-next";

import { Button } from "@wow-two-beta/fe/presentation/actions";
import { Field, TextInput } from "@wow-two-beta/fe/presentation/forms";

import { DeploymentFilters, RangePresets, type RangePreset } from "@/domain/deployments";

/**
 * One click for 24 hours to 12 months ending now, or a custom from and to with time in the owner's local time; a
 * custom range applies once both bounds are set and from comes first. Shared by the popover and the phone sheet.
 */
defineOptions({ name: "DateRangeChoices" });
const props = defineProps<DateRangeChoicesProps>();
const emit = defineEmits<{ choose: [value: DeploymentRange] }>();
const from = ref("");
const to = ref("");
const valid = computed(() => DeploymentFilters.isValidCustom(from.value, to.value));
const problem = computed(() => (from.value && to.value && !valid.value ? "Choose a start before the end." : null));

/** Opens on the range in force: a custom one as it is, a preset as the instants it covers now. */
onMounted(() => {
  if (props.modelValue.kind === "custom") {
    from.value = props.modelValue.from;
    to.value = props.modelValue.to;
    return;
  }
  const resolved = DeploymentFilters.resolve(props.modelValue, new Date());
  from.value = DeploymentFilters.localMinute(resolved.from);
  to.value = DeploymentFilters.localMinute(resolved.to);
});

function isChosen(preset: RangePreset): boolean {
  return props.modelValue.kind === "preset" && props.modelValue.preset === preset;
}

function apply(): void {
  if (valid.value) emit("choose", { kind: "custom", from: from.value, to: to.value });
}
</script>

<template>
  <div class="flex flex-col">
    <ul class="grid grid-cols-2 gap-1 p-2" aria-label="Ranges ending now">
      <li v-for="preset in RangePresets" :key="preset.value">
        <button
          type="button"
          class="flex min-h-11 w-full items-center justify-between gap-2 rounded-lg px-3 text-left hover:bg-muted/60 md:min-h-9"
          :aria-pressed="isChosen(preset.value)"
          @click="emit('choose', { kind: 'preset', preset: preset.value })"
        >
          <span>{{ preset.label }}</span>
          <Check v-if="isChosen(preset.value)" :size="14" class="text-primary" aria-hidden="true" />
        </button>
      </li>
    </ul>
    <form class="flex flex-col gap-3 border-t border-border p-3" @submit.prevent="apply">
      <p class="text-xs font-medium text-muted-foreground">Custom range, local time</p>
      <Field label="From">
        <TextInput v-model="from" type="datetime-local" size="sm" />
      </Field>
      <Field label="To">
        <TextInput v-model="to" type="datetime-local" size="sm" />
      </Field>
      <p v-if="problem" class="text-xs text-destructive" role="alert">{{ problem }}</p>
      <Button type="submit" size="sm" class="max-md:min-h-11" :is-disabled="!valid">Apply range</Button>
    </form>
  </div>
</template>
