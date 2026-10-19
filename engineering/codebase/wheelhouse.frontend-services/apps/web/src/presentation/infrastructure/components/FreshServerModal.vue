<script lang="ts">
import type { EnrollmentOperations } from "@/application/infrastructure";
import type { ServerOperations } from "@/application/servers";
import type { OneTimeKey } from "@/domain/infrastructure";
import type { Server } from "@/domain/servers";

/** Registers a fresh server by its public IP and creates its one-time enrollment key. */
export interface FreshServerModalProps {
  readonly open: boolean;
  readonly create: ServerOperations["create"];
  readonly createKey: EnrollmentOperations["createKey"];
}

/** A registered fresh server, with its one-time key when the host gave it. */
export interface FreshServerCreated {
  readonly server: Server;
  readonly oneTimeKey: OneTimeKey | null;
}
</script>

<script setup lang="ts">
import { Button } from "@wow-two-beta/fe/presentation/actions";
import { Alert } from "@wow-two-beta/fe/presentation/feedback";
import {
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
  ModalFooter,
  ModalHeader,
  ModalTitle,
} from "@wow-two-beta/fe/presentation/overlays";
import { useFormDialogSession } from "@wow-two-beta/fe/forms-engine";
import { useAppForm } from "@/bootstrap/form";
import {
  FreshServerProviders,
  freshServerFormOf,
  freshServerFormSchema,
  freshServerRequestOf,
} from "@/application/infrastructure/EnrollmentForms";
import { serverFieldPath } from "@/application/servers/ServerForms";
import { VpsProviderLabels } from "@/domain/servers";
import { failureReason } from "@/integration/common";

/**
 * A fresh server needs only who hosts it, where and its public IP: Wheelhouse registers it on that IP as its deploy
 * account with a products host's ingress, creates its one-time key and hands both on to the enrollment dialog.
 */
defineOptions({ name: "FreshServerModal" });
const props = defineProps<FreshServerModalProps>();
const emit = defineEmits<{ "update:open": [open: boolean]; created: [created: FreshServerCreated] }>();
defineSlots<{}>();

const form = useAppForm({
  defaultValues: freshServerFormOf(),
  schema: freshServerFormSchema,
  mapFieldPath: (path) => serverFieldPath(path).replace(/^host$/, "publicAddress"),
  onSubmit: async (values) => {
    const created = await props.create(freshServerRequestOf(values));
    if (!created.ok) return created;
    // The server exists now; a key the host did not give is asked for again by the enrollment dialog.
    const key = await props.createKey(created.value.slug);
    emit("created", {
      server: created.value,
      oneTimeKey: key.ok ? { publicKey: key.value.publicKey, fingerprint: key.value.fingerprint } : null,
    });
    emit("update:open", false);
    return { ok: true, value: undefined };
  },
});

useFormDialogSession(form, { open: () => props.open, initialValues: freshServerFormOf });
</script>

<template>
  <Modal :open="open" @update:open="emit('update:open', $event)">
    <ModalContent>
      <ModalHeader>
        <ModalTitle>Enroll a fresh server</ModalTitle>
      </ModalHeader>
      <form class="flex min-h-0 flex-1 flex-col" @submit="form.handleSubmit">
        <ModalBody class="flex flex-col gap-4">
          <div class="grid gap-4 sm:grid-cols-2">
            <form.Field name="slug" is-required v-slot="field">
              <Field label="Slug">
                <TextInput v-model="field.value" autocomplete="off" autocapitalize="off" placeholder="ocharo-vps" @blur="field.onBlur" />
              </Field>
            </form.Field>
            <form.Field name="name" is-required v-slot="field">
              <Field label="Name"><TextInput v-model="field.value" autocomplete="off" @blur="field.onBlur" /></Field>
            </form.Field>
            <form.Field name="provider" is-required v-slot="field">
              <Field label="Provider">
                <SelectPicker v-model="field.value"
                  ><SelectPickerTrigger aria-label="Provider"><SelectPickerValue /></SelectPickerTrigger
                  ><SelectPickerContent
                    ><SelectPickerItem
                      v-for="value in FreshServerProviders"
                      :key="value"
                      :item-key="value"
                      :label="VpsProviderLabels[value]" /></SelectPickerContent
                ></SelectPicker>
              </Field>
            </form.Field>
            <form.Field name="region" is-required v-slot="field">
              <Field label="Region">
                <TextInput v-model="field.value" autocomplete="off" placeholder="eu-central" @blur="field.onBlur" />
              </Field>
            </form.Field>
          </div>
          <form.Field name="publicAddress" is-required v-slot="field">
            <Field label="Public IP">
              <TextInput v-model="field.value" autocomplete="off" placeholder="203.0.113.10" @blur="field.onBlur" />
            </Field>
          </form.Field>
          <Alert v-if="form.state.submitError" severity="danger" :description="failureReason(form.state.submitError)" />
        </ModalBody>
        <ModalFooter>
          <Button type="button" variant="outline" tone="neutral" @click="emit('update:open', false)">Cancel</Button>
          <Button type="submit" :is-loading="form.state.isSubmitting">Add server</Button>
        </ModalFooter>
      </form>
    </ModalContent>
  </Modal>
</template>
