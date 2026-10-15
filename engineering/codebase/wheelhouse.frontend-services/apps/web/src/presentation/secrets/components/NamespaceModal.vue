<script lang="ts">
/** Namespace creation dialog in the currently selected vault. */
export interface NamespaceModalProps {
  vault: string;
  open: boolean;
}
</script>

<script setup lang="ts">
import { onBeforeUnmount, watch } from "vue";
import { Button } from "@wow-two-beta/ui-vue/presentation/actions";
import { Alert } from "@wow-two-beta/ui-vue/presentation/feedback";
import { Field, TextInput } from "@wow-two-beta/ui-vue/presentation/forms";
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
import { useNamespaceCreate } from "@/application/secrets";
import {
  createNamespaceForm,
  namespaceFormSchema,
} from "@/application/secrets/VaultForms";

/** Creates a namespace while preserving failed input for correction. */
defineOptions({ name: "NamespaceModal" });
const props = defineProps<NamespaceModalProps>();
const emit = defineEmits<{
  "update:open": [open: boolean];
  created: [slug: string];
}>();
defineSlots<{}>();

const creator = useNamespaceCreate(() => props.vault);
const form = useAppForm({
  defaultValues: createNamespaceForm(),
  schema: namespaceFormSchema,
  onSubmit: (values, context) =>
    creator.create(values.slug, values.name, context.signal),
});

/** Resets form ownership whenever its vault or open session changes. */
watch([() => props.open, () => props.vault], clear, { flush: "sync" });
onBeforeUnmount(clear);

/** Forgets the editor and aborts its pending submission. */
function clear(): void {
  form.invalidateSession(createNamespaceForm());
  creator.reset();
}

/** Closes the editor only on dismissal or confirmed success. */
function close(open: boolean): void {
  if (!open) clear();
  emit("update:open", open);
}

/** Submits validated input and selects the confirmed namespace. */
async function submit(event: Event): Promise<void> {
  if (await form.handleSubmit(event)) {
    emit("created", form.values.slug.trim());
    close(false);
  }
}
</script>

<template>
  <Modal :open="open" @update:open="close">
    <ModalContent class="w-full max-w-md">
      <form @submit="submit">
        <ModalHeader>
          <ModalTitle>New namespace</ModalTitle>
          <ModalDescription
            >Group one product environment's secrets and tokens
            together.</ModalDescription
          >
        </ModalHeader>
        <ModalBody class="flex flex-col gap-4">
          <form.Field name="slug" is-required v-slot="field">
            <Field label="Slug" helper="Lowercase, e.g. foreverpin-staging">
              <TextInput
                v-model="field.value"
                autocomplete="off"
                @blur="field.onBlur"
              />
            </Field>
          </form.Field>
          <form.Field name="name" is-required v-slot="field">
            <Field label="Name">
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
            >Create namespace</Button
          >
        </ModalFooter>
      </form>
    </ModalContent>
  </Modal>
</template>
