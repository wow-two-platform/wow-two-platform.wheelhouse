<script lang="ts">
import type { ServerOperations } from "@/application/servers";
import type { Server } from "@/domain/servers";

/** Server creation and editing; the slug is fixed once the server exists. */
export interface ServerFormModalProps {
  readonly open: boolean;
  /** The server to edit, or null to add one. */
  readonly server: Server | null;
  readonly operations: ServerOperations;
}
</script>

<script setup lang="ts">
import { ref } from "vue";
import { Button } from "@wow-two-beta/fe/presentation/actions";
import { Alert } from "@wow-two-beta/fe/presentation/feedback";
import {
  Field,
  NumberInput,
  SelectPicker,
  SelectPickerContent,
  SelectPickerItem,
  SelectPickerTrigger,
  SelectPickerValue,
  TagsInput,
  TextInput,
} from "@wow-two-beta/fe/presentation/forms";
import {
  Modal,
  ModalBody,
  ModalContent,
  ModalFooter,
  ModalHeader,
  ModalTitle,
  ModalSize,
} from "@wow-two-beta/fe/presentation/overlays";
import { useFormDialogSession } from "@wow-two-beta/fe/forms-engine";
import { useAppForm } from "@/bootstrap/form";
import {
  serverFieldPath,
  serverFormOf,
  serverFormSchema,
  serverRequestOf,
} from "@/application/servers/ServerForms";
import { VpsProvider, VpsProviderLabels } from "@/domain/servers";
import { failureReason } from "@/integration/common";
import { DeleteConfirm } from "@/presentation/common/components";

/** Edits how Wheelhouse reaches a server and how its ingress publishes sites. The SSH identity and pinned host key
 * stay files on the control host, so a new server reaches nothing until the owner places them. */
defineOptions({ name: "ServerFormModal" });
const props = defineProps<ServerFormModalProps>();
const emit = defineEmits<{ "update:open": [open: boolean] }>();
defineSlots<{}>();

const removeError = ref("");
const form = useAppForm({
  defaultValues: serverFormOf(null),
  schema: serverFormSchema,
  mapFieldPath: serverFieldPath,
  onSubmit: async (values) => {
    const body = serverRequestOf(values, !props.server);
    const result = props.server
      ? await props.operations.update(props.server.slug, body)
      : await props.operations.create(body);
    if (result.ok) emit("update:open", false);
    return result;
  },
});

/** Starts each session from the server being edited, or a Hetzner default, once the opening update has set both
 * props. */
useFormDialogSession(form, {
  open: () => props.open,
  initialValues: () => serverFormOf(props.server),
  onOpen: () => (removeError.value = ""),
});

/** Removes the server once nothing runs on it. */
async function remove(): Promise<void> {
  if (!props.server) return;
  removeError.value = "";
  const result = await props.operations.remove(props.server.slug);
  if (!result.ok) removeError.value = failureReason(result.failure);
  else emit("update:open", false);
}
</script>

<template>
  <Modal :open="open" @update:open="emit('update:open', $event)">
    <ModalContent :size="ModalSize.Lg">
      <ModalHeader>
        <ModalTitle>{{ server ? `Edit ${server.name}` : "New server" }}</ModalTitle>
      </ModalHeader>
      <form class="flex min-h-0 flex-1 flex-col" @submit="form.handleSubmit">
        <ModalBody class="flex flex-col gap-4">
          <div class="grid gap-4 sm:grid-cols-2">
            <form.Field v-if="!server" name="slug" is-required v-slot="field">
              <Field label="Slug" helper="Its SSH identity goes in ssh/<slug>/ on the control host">
                <TextInput v-model="field.value" autocomplete="off" placeholder="hel1" @blur="field.onBlur" />
              </Field>
            </form.Field>
            <form.Field name="name" is-required v-slot="field">
              <Field label="Name"><TextInput v-model="field.value" autocomplete="off" @blur="field.onBlur" /></Field>
            </form.Field>
            <form.Field name="provider" is-required v-slot="field">
              <Field label="Provider">
                <SelectPicker
                  v-model="field.value"
                  ><SelectPickerTrigger aria-label="Provider"><SelectPickerValue /></SelectPickerTrigger
                  ><SelectPickerContent
                    ><SelectPickerItem
                      v-for="value in Object.values(VpsProvider)"
                      :key="value"
                      :item-key="value"
                      :label="VpsProviderLabels[value]"
                  /></SelectPickerContent
                ></SelectPicker>
              </Field>
            </form.Field>
            <form.Field name="region" is-required v-slot="field">
              <Field label="Region">
                <TextInput v-model="field.value" autocomplete="off" placeholder="hel1" @blur="field.onBlur" />
              </Field>
            </form.Field>
          </div>
          <div class="grid gap-4 sm:grid-cols-[minmax(0,1fr)_9rem_7rem]">
            <form.Field name="host" is-required v-slot="field">
              <Field label="Host">
                <TextInput v-model="field.value" autocomplete="off" placeholder="vps.example.net" @blur="field.onBlur" />
              </Field>
            </form.Field>
            <form.Field name="sshUser" is-required v-slot="field">
              <Field label="SSH user"><TextInput v-model="field.value" autocomplete="off" @blur="field.onBlur" /></Field>
            </form.Field>
            <form.Field name="sshPort" is-required v-slot="field">
              <Field label="SSH port"><NumberInput v-model="field.value" @blur="field.onBlur" /></Field>
            </form.Field>
          </div>
          <fieldset class="flex flex-col gap-4">
            <legend class="mb-1 text-sm font-medium">Ingress</legend>
            <div class="grid gap-4 sm:grid-cols-3">
              <form.Field name="scheme" v-slot="field">
                <Field label="Scheme">
                  <SelectPicker v-model="field.value"
                    ><SelectPickerTrigger aria-label="Scheme"><SelectPickerValue /></SelectPickerTrigger
                    ><SelectPickerContent
                      ><SelectPickerItem item-key="https" label="https" /><SelectPickerItem item-key="http" label="http"
                    /></SelectPickerContent
                  ></SelectPicker>
                </Field>
              </form.Field>
              <form.Field name="port" v-slot="field">
                <Field label="Port"><NumberInput v-model="field.value" @blur="field.onBlur" /></Field>
              </form.Field>
              <form.Field name="certResolver" v-slot="field">
                <Field label="Certificate resolver">
                  <TextInput v-model="field.value" autocomplete="off" @blur="field.onBlur" />
                </Field>
              </form.Field>
            </div>
            <div class="grid gap-4 sm:grid-cols-2">
              <form.Field name="entryPoints" v-slot="field">
                <Field label="Entry points"><TagsInput v-model="field.value" placeholder="websecure" /></Field>
              </form.Field>
              <form.Field name="privateEntryPoints" v-slot="field">
                <Field label="Private entry points"><TagsInput v-model="field.value" /></Field>
              </form.Field>
            </div>
            <form.Field name="pattern" v-slot="field">
              <Field label="Host pattern" helper="For sites a target leaves unnamed; prod on a VPS names its own">
                <TextInput
                  v-model="field.value"
                  autocomplete="off"
                  placeholder="{site}-{product}.{environment}.preview.example"
                  @blur="field.onBlur"
                />
              </Field>
            </form.Field>
            <div class="grid gap-4 sm:grid-cols-2">
              <form.Field name="probe" v-slot="field">
                <Field label="Probe">
                  <TextInput v-model="field.value" autocomplete="off" placeholder="http://ingress:80" @blur="field.onBlur" />
                </Field>
              </form.Field>
              <form.Field name="privateProbe" v-slot="field">
                <Field label="Private probe"><TextInput v-model="field.value" autocomplete="off" @blur="field.onBlur" /></Field>
              </form.Field>
            </div>
          </fieldset>
          <Alert
            v-if="removeError || form.state.submitError"
            severity="danger"
            :description="removeError || failureReason(form.state.submitError)"
          />
        </ModalBody>
        <ModalFooter class="sm:justify-between">
          <DeleteConfirm v-if="server" label="Delete server" :question="`Delete ${server.name}?`" :on-confirm="remove" />
          <span v-else class="max-sm:hidden" />
          <span class="flex flex-col-reverse gap-2 sm:flex-row">
            <Button type="button" variant="outline" tone="neutral" @click="emit('update:open', false)">Cancel</Button>
            <Button type="submit" :is-loading="form.state.isSubmitting">{{ server ? "Save" : "Add server" }}</Button>
          </span>
        </ModalFooter>
      </form>
    </ModalContent>
  </Modal>
</template>
