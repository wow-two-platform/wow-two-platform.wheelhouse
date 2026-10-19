<script lang="ts">
/** Defines the dialog that adds a setting or secret key to a product beside the keys its releases declare. */
export interface AddKeyModalProps {
  readonly open: boolean;
  readonly product: string;
  /** Whether the key is a secret: the Secrets tab adds secrets, the Settings tab plain settings. */
  readonly secret: boolean;
}
</script>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { Button } from "@wow-two-beta/fe/presentation/actions";
import { Alert } from "@wow-two-beta/fe/presentation/feedback";
import {
  CheckboxField,
  Field,
  SelectPicker,
  SelectPickerContent,
  SelectPickerItem,
  SelectPickerTrigger,
  SelectPickerValue,
  TextInput,
} from "@wow-two-beta/fe/presentation/forms";
import {
  Modal,
  ModalBody,
  ModalContent,
  ModalDescription,
  ModalFooter,
  ModalHeader,
  ModalTitle,
} from "@wow-two-beta/fe/presentation/overlays";
import { useSettingKeys } from "@/application/settings";
import { SettingTypes, type SettingType } from "@/domain/settings";
import { failureReason } from "@/integration/common";

/**
 * Adds a key every environment of the product takes: its type, choices or date format, whether a deploy waits for it,
 * and a note. A plain key whose name reads as a secret needs the owner to confirm it is plain.
 */
defineOptions({ name: "AddKeyModal" });
const props = defineProps<AddKeyModalProps>();
const emit = defineEmits<{ "update:open": [open: boolean] }>();
defineSlots<{}>();

const keys = useSettingKeys();
const key = ref("");
const type = ref<SettingType>("text");
const choices = ref("");
const format = ref(SettingTypes.defaultDateFormat);
const required = ref(false);
const note = ref("");
const confirmPlain = ref(false);

watch(
  () => props.open,
  (open) => {
    if (!open) return;
    key.value = "";
    type.value = "text";
    choices.value = "";
    format.value = SettingTypes.defaultDateFormat;
    required.value = false;
    note.value = "";
    confirmPlain.value = false;
    keys.set.reset();
  },
);

const looksSecret = computed(() => !props.secret && SettingTypes.looksSecret(key.value.trim()));
const choiceList = computed(() => choices.value.split(/[\n,]/).map((item) => item.trim()).filter(Boolean));
const problem = computed(() => {
  const name = key.value.trim();
  if (!name) return null;
  if (!SettingTypes.isKey(name)) return "A key is a configuration path such as Section:Key.";
  if (type.value === "choice" && !choiceList.value.length) return "List the choices, one per line or comma-separated.";
  if (type.value === "date" && !SettingTypes.isDateFormat(format.value)) return "Use yyyy, MM, dd, HH, mm and ss, such as yyyy-MM-dd.";
  if (looksSecret.value && !confirmPlain.value) return `${name} reads as a secret; confirm it is a plain setting.`;
  return null;
});
const canSave = computed(() => Boolean(key.value.trim()) && !problem.value && !keys.set.loading.value);

async function save(event: Event): Promise<void> {
  event.preventDefault();
  if (!canSave.value) return;
  const result = await keys.set.mutateAsync({
    product: props.product,
    key: key.value.trim(),
    body: {
      type: type.value,
      secret: props.secret,
      required: required.value,
      ...(type.value === "choice" ? { choices: choiceList.value } : {}),
      ...(type.value === "date" ? { format: format.value } : {}),
      ...(note.value.trim() ? { note: note.value.trim() } : {}),
      ...(confirmPlain.value ? { confirmPlain: true } : {}),
    },
  });
  if (result.ok) emit("update:open", false);
}
</script>

<template>
  <Modal :open="props.open" @update:open="(open: boolean) => emit('update:open', open)">
    <ModalContent class="w-full max-w-lg">
      <form @submit="save">
        <ModalHeader>
          <ModalTitle>{{ props.secret ? "Add a secret" : "Add a setting" }}</ModalTitle>
          <ModalDescription>
            Every environment of this product takes it, beside the keys its releases declare. Give its value per
            environment once it is added.
          </ModalDescription>
        </ModalHeader>
        <ModalBody class="flex flex-col gap-4">
          <Field label="Key" helper="A configuration path, such as Billing:Mode" is-required>
            <TextInput v-model="key" autocomplete="off" spellcheck="false" />
          </Field>
          <Field label="Type">
            <SelectPicker :model-value="type" @update:model-value="(value: string | null) => (type = (value ?? 'text') as SettingType)">
              <SelectPickerTrigger aria-label="Type"><SelectPickerValue /></SelectPickerTrigger>
              <SelectPickerContent>
                <SelectPickerItem v-for="name in SettingTypes.names" :key="name" :item-key="name" :label="name" />
              </SelectPickerContent>
            </SelectPicker>
          </Field>
          <Field v-if="type === 'choice'" label="Choices" helper="One per line or comma-separated">
            <TextInput v-model="choices" autocomplete="off" />
          </Field>
          <Field v-if="type === 'date'" label="Format" helper="yyyy, MM, dd, HH, mm, ss">
            <TextInput v-model="format" autocomplete="off" spellcheck="false" />
          </Field>
          <Field label="Note" helper="What the key is for">
            <TextInput v-model="note" autocomplete="off" />
          </Field>
          <CheckboxField v-model="required" label="Required: a deploy waits until it has a value" />
          <CheckboxField v-if="looksSecret" v-model="confirmPlain" :label="`${key.trim()} reads as a secret; it is a plain setting`" />
          <Alert v-if="problem" severity="warning" :description="problem" />
          <Alert v-if="keys.set.error.value" severity="danger" :description="failureReason(keys.set.error.value)" />
        </ModalBody>
        <ModalFooter>
          <Button type="button" variant="outline" tone="neutral" @click="emit('update:open', false)">Cancel</Button>
          <Button type="submit" :is-disabled="!canSave" :is-loading="keys.set.loading.value">Add key</Button>
        </ModalFooter>
      </form>
    </ModalContent>
  </Modal>
</template>
