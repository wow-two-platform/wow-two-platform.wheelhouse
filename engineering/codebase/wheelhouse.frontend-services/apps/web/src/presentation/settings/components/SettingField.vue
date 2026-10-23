<script lang="ts">
import type { FieldSave } from "@/application/settings";
import type { SettingType } from "@/domain/settings";

/** Defines one key's field that saves on its own: its declaration, what it holds and its save state. */
export interface SettingFieldProps {
  /** The configuration path. */
  readonly settingKey: string;
  readonly type: SettingType;
  readonly secret: boolean;
  readonly required?: boolean;
  readonly format?: string | null;
  readonly choices?: readonly string[] | null;
  /** A plain setting's current value; a secret's is never known. */
  readonly current?: string | null;
  /** The release's default, shown as the placeholder. */
  readonly defaultValue?: string | null;
  /** The line under the label: who takes the key and where its value comes from. */
  readonly helper?: string | null;
  /** Whether its name reads as a secret: saving it plain asks for a confirmation. */
  readonly looksSecret?: boolean;
  /** The last save's state. */
  readonly save?: FieldSave | null;
  readonly isDisabled?: boolean;
}
</script>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { Check, Dices, LoaderCircle, TriangleAlert } from "lucide-vue-next";
import { Button } from "@wow-two-beta/fe/presentation/actions";
import { CheckboxField, Field } from "@wow-two-beta/fe/presentation/forms";
import { SettingTypes } from "@/domain/settings";
import SettingInput from "./SettingInput.vue";

/**
 * One key's field, saved as soon as it is confirmed — on blur, Enter or a pick — when its value changed and fits its
 * type; the field then shows saving, saved or why it failed. A secret's field empties once saved, since its value never
 * comes back, and says it is set in the vault. A text secret also offers Generate: Wheelhouse makes a random value
 * straight into the vault, and the browser never sees it. A key that reads as a secret, given as a plain setting, waits
 * for the owner to confirm it is plain.
 */
defineOptions({ name: "SettingField" });
const props = withDefaults(defineProps<SettingFieldProps>(), {
  required: false,
  format: null,
  choices: null,
  current: null,
  defaultValue: null,
  helper: null,
  looksSecret: false,
  save: null,
  isDisabled: false,
});
const emit = defineEmits<{ save: [value: string, confirmPlain: boolean]; generate: [] }>();
defineSlots<{}>();

const value = ref(props.current ?? "");
const confirmPlain = ref(false);
/** The value last sent, so a blur without a change saves nothing. */
const sent = ref(props.current ?? "");

/* A value read again from the server replaces the field's, unless the owner is mid-edit. */
watch(
  () => props.current,
  (current) => {
    if (value.value === sent.value) value.value = current ?? "";
    sent.value = current ?? "";
  },
);
/* A saved secret's field empties: its value never comes back to the browser. */
watch(
  () => props.save?.state,
  (state) => {
    if (state === "saved" && props.secret) {
      value.value = "";
      sent.value = "";
    }
  },
);

const problem = computed(() =>
  value.value.trim() ? SettingTypes.check(props.type, value.value.trim(), props.format, props.choices) : null,
);
/** The field's helper and error, each only when there is one. @internal */
const notes = computed(() => {
  const error = problem.value
    ? `${props.settingKey} ${problem.value}`
    : props.save?.state === "failed"
      ? (props.save.message ?? "Not saved.")
      : null;
  return { ...(props.helper ? { helper: props.helper } : {}), ...(error ? { error } : {}) };
});
const needsConfirm = computed(() => !props.secret && props.looksSecret && !confirmPlain.value);
const canGenerate = computed(() => props.secret && SettingTypes.canGenerate(props.type));

/** Asks Wheelhouse for a random value in the vault; whatever the field held is dropped. @internal */
function generate(): void {
  if (props.isDisabled || props.save?.state === "saving") return;
  value.value = "";
  sent.value = "";
  emit("generate");
}

/** Saves a changed value that fits its type. @internal */
function confirm(): void {
  const next = props.type === "boolean" ? value.value.trim().toLowerCase() : value.value;
  if (!next.trim() || next === sent.value || problem.value || needsConfirm.value || props.isDisabled) return;
  sent.value = next;
  emit("save", next, confirmPlain.value);
}
</script>

<template>
  <div class="flex flex-col gap-1.5">
    <Field :label="props.settingKey" v-bind="notes" :is-required="props.required">
      <div class="flex items-center gap-2">
        <div class="min-w-0 flex-1">
          <SettingInput
            v-model="value"
            :label="props.settingKey"
            :type="props.type"
            :secret="props.secret"
            :format="props.format"
            :choices="props.choices"
            :placeholder="props.secret ? (props.save?.state === 'saved' ? 'Set in the vault' : '') : (props.defaultValue ?? null)"
            :is-disabled="props.isDisabled"
            @confirm="confirm"
          />
        </div>
        <Button
          v-if="canGenerate"
          type="button"
          variant="outline"
          tone="neutral"
          size="sm"
          class="shrink-0 max-md:min-h-11"
          :is-disabled="props.isDisabled || props.save?.state === 'saving'"
          :aria-label="`Generate ${props.settingKey} in the vault`"
          title="32 random bytes, kept in the vault and never shown"
          @click="generate"
          ><template #leading><Dices :size="14" aria-hidden="true" /></template><span class="max-sm:sr-only">Generate</span></Button
        >
        <span class="flex w-16 shrink-0 items-center gap-1 text-xs" aria-live="polite">
          <template v-if="props.save?.state === 'saving'"
            ><LoaderCircle :size="14" class="animate-spin text-muted-foreground" aria-hidden="true" /><span
              class="text-muted-foreground"
              >Saving</span
            ></template
          >
          <template v-else-if="props.save?.state === 'saved'"
            ><Check :size="14" class="text-success" aria-hidden="true" /><span class="text-success"
              >Saved{{ props.save.version ? ` v${props.save.version}` : "" }}</span
            ></template
          >
          <template v-else-if="props.save?.state === 'failed'"
            ><TriangleAlert :size="14" class="text-destructive" aria-hidden="true" /><span class="text-destructive"
              >Failed</span
            ></template
          >
        </span>
      </div>
    </Field>
    <CheckboxField
      v-if="!props.secret && props.looksSecret"
      v-model="confirmPlain"
      :disabled="props.isDisabled"
      :label="`${props.settingKey} reads as a secret; it is a plain setting`"
      @update:model-value="(checked: boolean) => checked && confirm()"
    />
  </div>
</template>
