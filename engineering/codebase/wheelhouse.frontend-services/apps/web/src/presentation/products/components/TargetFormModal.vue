<script lang="ts">
import type { TargetOperations } from "@/application/targets";
import type { Server } from "@/domain/servers";
import type { InventoryTarget } from "@/domain/targets";

/** Environment creation and editing for one product; the slug is fixed once the target exists. */
export interface TargetFormModalProps {
  readonly open: boolean;
  /** The product the environment belongs to. */
  readonly product: string;
  /** The target to edit, or null to add one. */
  readonly target: InventoryTarget | null;
  readonly servers: readonly Server[];
  readonly operations: Pick<TargetOperations, "create" | "update" | "remove">;
}
</script>

<script setup lang="ts">
import { ref, watch } from "vue";
import { Button } from "@wow-two-beta/ui-vue/presentation/actions";
import { Alert } from "@wow-two-beta/ui-vue/presentation/feedback";
import {
  Field,
  SelectPicker,
  SelectPickerContent,
  SelectPickerItem,
  SelectPickerTrigger,
  SelectPickerValue,
  TextInput,
} from "@wow-two-beta/ui-vue/presentation/forms";
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
  suggestedTargetSlug,
  targetFormOf,
  targetFormSchema,
  targetRequestOf,
} from "@/application/targets/TargetForms";
import { DeploymentEnvironment } from "@/domain/targets";
import { failureReason } from "@/integration/common";
import { DeleteConfirm, RowsField } from "@/presentation/common/components";

/** Edits where an environment runs, which settings files its services read, which requests prove a rollout and
 * which hosts its sites answer on. */
defineOptions({ name: "TargetFormModal" });
const props = defineProps<TargetFormModalProps>();
const emit = defineEmits<{ "update:open": [open: boolean] }>();
defineSlots<{}>();

const environments = Object.values(DeploymentEnvironment);
const removeError = ref("");
const form = useAppForm({
  defaultValues: targetFormOf(null, props.product, null),
  schema: targetFormSchema,
  onSubmit: async (values) => {
    const body = targetRequestOf(values, props.product, !props.target);
    const result = props.target
      ? await props.operations.update(props.target.slug, body)
      : await props.operations.create(body);
    if (result.ok) emit("update:open", false);
    return result;
  },
});

/** Starts each session from the target being edited, or a dev environment on the first server. */
watch(
  () => props.open,
  (open) => {
    if (!open) return;
    removeError.value = "";
    form.invalidateSession(targetFormOf(props.target, props.product, props.servers[0]?.slug ?? null));
  },
  { immediate: true, flush: "sync" },
);

/** Keeps a new target's slug following its environment until the operator types another. */
watch(
  () => form.state.values.environment,
  (environment, previous) => {
    if (props.target || !previous) return;
    if (form.state.values.slug === suggestedTargetSlug(props.product, previous))
      form.setValue("slug", suggestedTargetSlug(props.product, environment));
  },
);

/** Removes the environment from the inventory; what runs on its host stays until torn down there. */
async function remove(): Promise<void> {
  if (!props.target) return;
  removeError.value = "";
  const result = await props.operations.remove(props.target.slug);
  if (!result.ok) removeError.value = failureReason(result.failure);
  else emit("update:open", false);
}
</script>

<template>
  <Modal :open="open" @update:open="emit('update:open', $event)">
    <ModalContent class="flex max-h-[calc(100dvh-2rem)] w-[min(44rem,calc(100vw-2rem))] flex-col">
      <ModalHeader>
        <ModalTitle>{{ target ? `Edit ${target.slug}` : "New environment" }}</ModalTitle>
      </ModalHeader>
      <form class="flex min-h-0 flex-1 flex-col" @submit="form.handleSubmit">
        <ModalBody class="-mx-1 flex min-h-0 flex-1 flex-col gap-4 overflow-y-auto px-1">
          <div class="grid gap-4 sm:grid-cols-2">
            <form.Field name="environment" is-required v-slot="field">
              <Field label="Environment">
                <SelectPicker v-model="field.value"
                  ><SelectPickerTrigger aria-label="Environment"><SelectPickerValue /></SelectPickerTrigger
                  ><SelectPickerContent
                    ><SelectPickerItem v-for="value in environments" :key="value" :item-key="value" :label="value"
                  /></SelectPickerContent
                ></SelectPicker>
              </Field>
            </form.Field>
            <form.Field name="server" is-required v-slot="field">
              <Field label="Server">
                <SelectPicker v-model="field.value"
                  ><SelectPickerTrigger aria-label="Server"
                    ><SelectPickerValue placeholder="Choose a server" /></SelectPickerTrigger
                  ><SelectPickerContent
                    ><SelectPickerItem
                      v-for="server in servers"
                      :key="server.slug"
                      :item-key="server.slug"
                      :label="server.name"
                  /></SelectPickerContent
                ></SelectPicker>
              </Field>
            </form.Field>
          </div>
          <form.Field v-if="!target" name="slug" is-required v-slot="field">
            <Field label="Slug" helper="Deployments know the environment by this; it never changes">
              <TextInput v-model="field.value" autocomplete="off" @blur="field.onBlur" />
            </Field>
          </form.Field>
          <div class="grid gap-4 sm:grid-cols-2">
            <form.Field name="network" is-required v-slot="field">
              <Field label="Network"><TextInput v-model="field.value" autocomplete="off" @blur="field.onBlur" /></Field>
            </form.Field>
            <form.Field name="root" is-required v-slot="field">
              <Field label="Releases folder"><TextInput v-model="field.value" autocomplete="off" @blur="field.onBlur" /></Field>
            </form.Field>
          </div>
          <RowsField
            :form="form"
            name="settings"
            label="Settings files"
            add-label="Add service"
            :blank="{ service: '', path: '' }"
            :columns="[
              { key: 'service', label: 'Service', placeholder: 'api' },
              { key: 'path', label: 'Path on the host', placeholder: '/srv/settings/product/api.json' },
            ]"
          />
          <RowsField
            :form="form"
            name="smokeChecks"
            label="Smoke checks"
            add-label="Add check"
            :blank="{ service: '', path: '/health', status: 200 }"
            :columns="[
              { key: 'service', label: 'Service', placeholder: 'api' },
              { key: 'path', label: 'Path', placeholder: '/health' },
              { key: 'status', label: 'Status', isNumeric: true },
            ]"
          />
          <RowsField
            :form="form"
            name="sites"
            label="Site hosts"
            add-label="Add site"
            :blank="{ site: '', host: '' }"
            :columns="[
              { key: 'site', label: 'Site', placeholder: 'app' },
              { key: 'host', label: 'Host', placeholder: 'app.example.com' },
            ]"
          />
          <Alert
            v-if="removeError || form.state.submitError"
            severity="danger"
            :description="removeError || failureReason(form.state.submitError)"
          />
        </ModalBody>
        <ModalFooter class="justify-between">
          <DeleteConfirm
            v-if="target"
            label="Remove environment"
            :question="`Remove ${target.slug}?`"
            :on-confirm="remove"
          />
          <span v-else />
          <span class="flex gap-2">
            <Button type="button" variant="outline" tone="neutral" @click="emit('update:open', false)">Cancel</Button>
            <Button type="submit" :is-loading="form.state.isSubmitting">{{ target ? "Save" : "Add environment" }}</Button>
          </span>
        </ModalFooter>
      </form>
    </ModalContent>
  </Modal>
</template>
