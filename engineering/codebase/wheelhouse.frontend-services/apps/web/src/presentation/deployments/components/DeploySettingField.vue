<script lang="ts">
import type { FieldSave } from "@/application/settings";
import type { PromptedSetting } from "@/domain/deployments";

/** Defines one key's field in the deploy dialog: its declaration, the value it holds and whether it may go to the vault. */
export interface DeploySettingFieldProps {
  /** The key, with what the release declares about it. */
  readonly item: PromptedSetting;
  /** Whether the value goes to the vault. */
  readonly secret: boolean;
  /** A plain setting's saved value, which the field starts with; a secret's is never known. */
  readonly current?: string | null;
  /** The last save's state. */
  readonly save?: FieldSave | null;
  /** Whether the owner may keep the value in the vault: not for a declared secret, nor once a kind is set. */
  readonly canChooseVault?: boolean;
  /** Whether the controls take input. */
  readonly isDisabled?: boolean;
}
</script>

<script setup lang="ts">
import { CheckboxField } from "@wow-two-beta/fe/presentation/forms";

import SettingField from "@/presentation/settings/components/SettingField.vue";

/** One key's field that saves on its own, with the line saying who takes it and the choice to keep it in the vault. */
defineOptions({ name: "DeploySettingField" });
const props = withDefaults(defineProps<DeploySettingFieldProps>(), {
  current: null,
  save: null,
  canChooseVault: false,
  isDisabled: false,
});
const emit = defineEmits<{ save: [value: string, confirmPlain: boolean]; "update:secret": [value: boolean] }>();
defineSlots<{}>();

/** Who takes the key, and its type and default. @internal */
function helper(): string {
  const { item } = props;
  const where = item.services.join(", ");
  const kind = item.type && item.type !== "text" ? `${item.type}${item.format ? ` (${item.format})` : ""}; ` : "";
  if (item.required) return `${kind}Required by ${where}`;
  return item.default !== null ? `${kind}Optional for ${where}; default ${item.default || "empty"}` : `${kind}Optional for ${where}`;
}
</script>

<template>
  <div class="flex flex-col gap-1.5">
    <SettingField
      :setting-key="props.item.key"
      :type="props.item.type ?? 'text'"
      :format="props.item.format ?? null"
      :choices="props.item.allowed"
      :secret="props.secret"
      :required="props.item.required"
      :current="props.current"
      :default-value="props.item.default"
      :helper="helper()"
      :looks-secret="props.item.looksSecret ?? false"
      :save="props.save"
      :is-disabled="props.isDisabled"
      @save="(value: string, confirmPlain: boolean) => emit('save', value, confirmPlain)"
    />
    <CheckboxField
      v-if="props.canChooseVault"
      :model-value="props.secret"
      :disabled="props.isDisabled || props.save?.state === 'saved'"
      label="Secret: keep it in the vault"
      @update:model-value="(value: boolean) => emit('update:secret', value)"
    />
  </div>
</template>
