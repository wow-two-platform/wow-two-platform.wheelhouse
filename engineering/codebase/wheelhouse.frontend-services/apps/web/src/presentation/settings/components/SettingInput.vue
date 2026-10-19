<script lang="ts">
import type { SettingType } from "@/domain/settings";

/** Defines a value's input by its type: a picker for a choice or a boolean, a masked one for a secret, text else. */
export interface SettingInputProps {
  /** The configuration path, the input's accessible name. */
  readonly label: string;
  readonly type: SettingType;
  readonly secret?: boolean;
  readonly format?: string | null;
  readonly choices?: readonly string[] | null;
  readonly placeholder?: string | null;
  readonly isDisabled?: boolean;
}
</script>

<script setup lang="ts">
import { computed } from "vue";
import {
  PasswordInput,
  SelectPicker,
  SelectPickerContent,
  SelectPickerItem,
  SelectPickerTrigger,
  SelectPickerValue,
  TextInput,
} from "@wow-two-beta/fe/presentation/forms";
import { SettingTypes } from "@/domain/settings";

/**
 * One value's input. A choice and a boolean pick from their values and confirm on pick; the others confirm on blur or
 * Enter. A secret's value is masked and never prefilled.
 */
defineOptions({ name: "SettingInput" });
const props = withDefaults(defineProps<SettingInputProps>(), {
  secret: false,
  format: null,
  choices: null,
  placeholder: null,
  isDisabled: false,
});
const model = defineModel<string>({ required: true });
const emit = defineEmits<{ confirm: [] }>();
defineSlots<{}>();

const picks = computed(() =>
  props.type === "boolean" ? ["true", "false"] : props.type === "choice" ? [...(props.choices ?? [])] : [],
);
const hint = computed(() => props.placeholder ?? SettingTypes.placeholder(props.type, props.format));

/** Takes a pick as the value and confirms it at once. @internal */
function pick(value: string | null): void {
  model.value = value ?? "";
  emit("confirm");
}
</script>

<template>
  <SelectPicker
    v-if="picks.length && !props.secret"
    :model-value="model || null"
    :is-disabled="props.isDisabled"
    @update:model-value="pick"
  >
    <SelectPickerTrigger :aria-label="props.label">
      <SelectPickerValue :placeholder="hint || 'Choose a value'" />
    </SelectPickerTrigger>
    <SelectPickerContent>
      <SelectPickerItem v-for="choice in picks" :key="choice" :item-key="choice" :label="choice" />
    </SelectPickerContent>
  </SelectPicker>
  <PasswordInput
    v-else-if="props.secret"
    v-model="model"
    :aria-label="props.label"
    :is-disabled="props.isDisabled"
    autocomplete="new-password"
    @blur="emit('confirm')"
    @keydown.enter.prevent="emit('confirm')"
  />
  <TextInput
    v-else
    v-model="model"
    :aria-label="props.label"
    :is-disabled="props.isDisabled"
    :placeholder="hint"
    :inputmode="props.type === 'number' ? 'decimal' : undefined"
    :type="props.type === 'url' ? 'url' : props.type === 'email' ? 'email' : 'text'"
    autocomplete="off"
    spellcheck="false"
    @blur="emit('confirm')"
    @keydown.enter.prevent="emit('confirm')"
  />
</template>
