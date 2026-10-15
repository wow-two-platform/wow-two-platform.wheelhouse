<script lang="ts">
/** Local secret editing session, optionally bound to an immutable rotation key. */
export interface SetSecretModalProps {
  vault: string;
  ns: string;
  secretKey?: string | undefined;
  open: boolean;
}
</script>

<script setup lang="ts">
import { onBeforeUnmount, watch } from "vue";
import { Button } from "@wow-two-beta/ui-vue/presentation/actions";
import { Alert } from "@wow-two-beta/ui-vue/presentation/feedback";
import {
  Field,
  PasswordInput,
  TextInput,
} from "@wow-two-beta/ui-vue/presentation/forms";
import {
  Modal,
  ModalContent,
  ModalHeader,
  ModalTitle,
  ModalDescription,
  ModalBody,
  ModalFooter,
} from "@wow-two-beta/ui-vue/presentation/overlays";
import { useAppForm } from "@/bootstrap/form";
import { useSecretChanges } from "@/application/secrets";
import {
  createSecretForm,
  secretFormSchema,
} from "@/application/secrets/VaultForms";

/** Writes a secret version without caching or persisting its plaintext. */
defineOptions({ name: "SetSecretModal" });
const props = defineProps<SetSecretModalProps>();
const emit = defineEmits<{ "update:open": [open: boolean] }>();
defineSlots<{}>();

const changes = useSecretChanges(
  () => props.vault,
  () => props.ns,
);
const form = useAppForm({
  defaultValues: createSecretForm(props.secretKey),
  schema: secretFormSchema,
  onSubmit: (values, context) =>
    changes.set(
      values.key,
      values.value,
      values.description || undefined,
      context.signal,
    ),
});

/** Discards plaintext immediately on close, target change, or a different rotation key. */
watch(
  [() => props.open, () => props.vault, () => props.ns, () => props.secretKey],
  clear,
  { flush: "sync" },
);
onBeforeUnmount(clear);

/** Invalidates pending work and empties both editable values and the form baseline. */
function clear(): void {
  form.invalidateSession(createSecretForm(props.secretKey));
  changes.reset();
}

/** Forgets the plaintext whenever the dialog is dismissed. */
function close(open: boolean): void {
  if (!open) clear();
  emit("update:open", open);
}

/** Closes only on confirmed success; failed input remains editable in the current session. */
async function submit(event: Event): Promise<void> {
  if (await form.handleSubmit(event)) close(false);
}
</script>

<template>
  <Modal :open="open" @update:open="close">
    <ModalContent>
      <form @submit="submit">
        <ModalHeader>
          <ModalTitle>{{
            secretKey ? `Rotate ${secretKey}` : "Add secret"
          }}</ModalTitle>
          <ModalDescription>
            The vault receives a new version. Wheelhouse only displays its
            metadata afterwards.
          </ModalDescription>
        </ModalHeader>
        <ModalBody class="flex flex-col gap-4">
          <form.Field v-if="!secretKey" name="key" is-required v-slot="field">
            <Field label="Key" helper="e.g. DATABASE_URL or Billing:SecretKey">
              <TextInput
                v-model="field.value"
                autocomplete="off"
                @blur="field.onBlur"
              />
            </Field>
          </form.Field>
          <form.Field name="value" is-required v-slot="field">
            <Field label="Value">
              <PasswordInput
                v-model="field.value"
                autocomplete="new-password"
                @blur="field.onBlur"
              />
            </Field>
          </form.Field>
          <form.Field name="description" v-slot="field">
            <Field label="Description">
              <TextInput
                v-model="field.value"
                autocomplete="off"
                @blur="field.onBlur"
              />
            </Field>
          </form.Field>
          <Alert
            v-if="form.state.submitError"
            severity="danger"
            :description="form.state.submitError.message"
          />
        </ModalBody>
        <ModalFooter>
          <Button
            type="button"
            variant="outline"
            tone="neutral"
            @click="close(false)"
            >Cancel</Button
          >
          <Button type="submit" :is-loading="form.state.isSubmitting"
            >Save version</Button
          >
        </ModalFooter>
      </form>
    </ModalContent>
  </Modal>
</template>
