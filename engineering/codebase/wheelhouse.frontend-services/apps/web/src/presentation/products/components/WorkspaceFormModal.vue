<script lang="ts">
import type { WorkspaceOperations } from "@/application/workspaces";
import type { Workspace } from "@/domain/workspaces";

/** Workspace creation and renaming; the slug is fixed once the workspace exists. */
export interface WorkspaceFormModalProps {
  readonly open: boolean;
  /** The workspace to rename, or null to add one. */
  readonly workspace: Workspace | null;
  readonly operations: Pick<WorkspaceOperations, "create" | "update">;
}
</script>

<script setup lang="ts">
import { Button } from "@wow-two-beta/fe/presentation/actions";
import { Alert } from "@wow-two-beta/fe/presentation/feedback";
import { Field, TextAreaInput, TextInput } from "@wow-two-beta/fe/presentation/forms";
import {
  Modal,
  ModalBody,
  ModalContent,
  ModalFooter,
  ModalHeader,
  ModalTitle,
} from "@wow-two-beta/fe/presentation/overlays";
import { useFormDialogSession } from "@wow-two-beta/fe/forms-engine";
import { useAppForm } from "@/bootstrap/form";
import {
  createWorkspaceRequestOf,
  updateWorkspaceRequestOf,
  workspaceFormOf,
  workspaceFormSchema,
} from "@/application/workspaces/WorkspaceForms";
import { failureReason } from "@/integration/common";

/** Adds a workspace, or renames and redescribes one. */
defineOptions({ name: "WorkspaceFormModal" });
const props = defineProps<WorkspaceFormModalProps>();
const emit = defineEmits<{ "update:open": [open: boolean] }>();
defineSlots<{}>();

const form = useAppForm({
  defaultValues: workspaceFormOf(null),
  schema: workspaceFormSchema,
  onSubmit: async (values) => {
    const result = props.workspace
      ? await props.operations.update(props.workspace.id, updateWorkspaceRequestOf(values))
      : await props.operations.create(createWorkspaceRequestOf(values));
    if (result.ok) emit("update:open", false);
    return result;
  },
});

/** Starts each session from the workspace being renamed, or blank. */
useFormDialogSession(form, {
  open: () => props.open,
  initialValues: () => workspaceFormOf(props.workspace),
});
</script>

<template>
  <Modal :open="open" @update:open="emit('update:open', $event)">
    <ModalContent>
      <ModalHeader>
        <ModalTitle>{{ workspace ? `Rename ${workspace.name}` : "New workspace" }}</ModalTitle>
      </ModalHeader>
      <form class="flex min-h-0 flex-1 flex-col" @submit="form.handleSubmit">
        <ModalBody class="flex flex-col gap-4">
          <form.Field v-if="!workspace" name="slug" is-required v-slot="field">
            <Field label="Slug" helper="Its address, /w/slug; it never changes">
              <TextInput v-model="field.value" autocomplete="off" placeholder="platform" @blur="field.onBlur" />
            </Field>
          </form.Field>
          <form.Field name="name" is-required v-slot="field">
            <Field label="Name"><TextInput v-model="field.value" autocomplete="off" @blur="field.onBlur" /></Field>
          </form.Field>
          <form.Field name="description" v-slot="field">
            <Field label="Description">
              <TextAreaInput v-model="field.value" :rows="2" @blur="field.onBlur" />
            </Field>
          </form.Field>
          <Alert v-if="form.state.submitError" severity="danger" :description="failureReason(form.state.submitError)" />
        </ModalBody>
        <ModalFooter class="justify-end gap-2">
          <Button type="button" variant="outline" tone="neutral" @click="emit('update:open', false)">Cancel</Button>
          <Button type="submit" :is-loading="form.state.isSubmitting">{{ workspace ? "Save" : "Add workspace" }}</Button>
        </ModalFooter>
      </form>
    </ModalContent>
  </Modal>
</template>
