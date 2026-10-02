<script lang="ts">
import type { VaultOperations } from "@/application/secrets";
import type { VaultDefinition } from "@/domain/secrets";

/** Vault creation and editing on one server; the slug is fixed once the vault exists. */
export interface VaultFormModalProps {
  readonly open: boolean;
  /** The server the vault runs on. */
  readonly server: string;
  /** The vault to edit, or null to add one. */
  readonly vault: VaultDefinition | null;
  readonly operations: Pick<VaultOperations, "create" | "update" | "remove">;
}
</script>

<script setup lang="ts">
import { ref, watch } from "vue";
import { Button } from "@wow-two-beta/ui-vue/presentation/actions";
import { Alert } from "@wow-two-beta/ui-vue/presentation/feedback";
import { Field, TextInput } from "@wow-two-beta/ui-vue/presentation/forms";
import {
  Modal,
  ModalBody,
  ModalContent,
  ModalFooter,
  ModalHeader,
  ModalTitle,
} from "@wow-two-beta/ui-vue/presentation/overlays";
import { useAppForm } from "@/bootstrap/form";
import {
  vaultDefinitionFormOf,
  vaultDefinitionFormSchema,
  vaultDefinitionRequestOf,
} from "@/application/secrets/VaultDefinitionForms";
import { failureReason } from "@/integration/common";
import { DeleteConfirm } from "@/presentation/common/components";

/** Edits where Wheelhouse reaches a vault. Its administrator password stays a file on the control host, so a new
 * vault opens nothing until the operator places it. */
defineOptions({ name: "VaultFormModal" });
const props = defineProps<VaultFormModalProps>();
const emit = defineEmits<{ "update:open": [open: boolean] }>();
defineSlots<{}>();

const removeError = ref("");
const form = useAppForm({
  defaultValues: vaultDefinitionFormOf(null, props.server),
  schema: vaultDefinitionFormSchema,
  onSubmit: async (values) => {
    const server = props.vault?.server ?? props.server;
    const body = vaultDefinitionRequestOf(values, server, !props.vault);
    const result = props.vault
      ? await props.operations.update(props.vault.slug, body)
      : await props.operations.create(body);
    if (result.ok) emit("update:open", false);
    return result;
  },
});

/** Starts each session from the vault being edited, or one named after its server. */
watch(
  () => props.open,
  (open) => {
    if (!open) return;
    removeError.value = "";
    form.invalidateSession(vaultDefinitionFormOf(props.vault, props.server));
  },
  { immediate: true, flush: "sync" },
);

/** Stops administering the vault; the vault and its secrets stay where they run. */
async function remove(): Promise<void> {
  if (!props.vault) return;
  removeError.value = "";
  const result = await props.operations.remove(props.vault.slug);
  if (!result.ok) removeError.value = failureReason(result.failure);
  else emit("update:open", false);
}
</script>

<template>
  <Modal :open="open" @update:open="emit('update:open', $event)">
    <ModalContent class="flex max-h-[calc(100dvh-2rem)] w-[min(32rem,calc(100vw-2rem))] flex-col">
      <ModalHeader>
        <ModalTitle>{{ vault ? `Edit ${vault.name}` : "New vault" }}</ModalTitle>
      </ModalHeader>
      <form class="flex min-h-0 flex-1 flex-col" @submit="form.handleSubmit">
        <ModalBody class="-mx-1 flex min-h-0 flex-1 flex-col gap-4 overflow-y-auto px-1">
          <form.Field v-if="!vault" name="slug" is-required v-slot="field">
            <Field label="Slug" helper="Its password goes in vaults/<slug>/password on the control host">
              <TextInput v-model="field.value" autocomplete="off" @blur="field.onBlur" />
            </Field>
          </form.Field>
          <form.Field name="name" is-required v-slot="field">
            <Field label="Name"><TextInput v-model="field.value" autocomplete="off" @blur="field.onBlur" /></Field>
          </form.Field>
          <form.Field name="url" is-required v-slot="field">
            <Field label="Endpoint" helper="Reached from the control host, never from the browser">
              <TextInput v-model="field.value" autocomplete="off" @blur="field.onBlur" />
            </Field>
          </form.Field>
          <Alert
            v-if="removeError || form.state.submitError"
            severity="danger"
            :description="removeError || failureReason(form.state.submitError)"
          />
        </ModalBody>
        <ModalFooter class="justify-between">
          <DeleteConfirm v-if="vault" label="Remove vault" :question="`Remove ${vault.name}?`" :on-confirm="remove" />
          <span v-else />
          <span class="flex gap-2">
            <Button type="button" variant="outline" tone="neutral" @click="emit('update:open', false)">Cancel</Button>
            <Button type="submit" :is-loading="form.state.isSubmitting">{{ vault ? "Save" : "Add vault" }}</Button>
          </span>
        </ModalFooter>
      </form>
    </ModalContent>
  </Modal>
</template>
