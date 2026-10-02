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
import { reactive, ref, watch } from "vue";
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
import type { SaveVaultRequest } from "@/domain/secrets";
import { failureMessages } from "@/integration/common";
import { DeleteConfirm } from "@/presentation/common/components";

/** Edits where Wheelhouse reaches a vault. Its administrator password stays a file on the control host, so a new
 * vault opens nothing until the operator places it. */
defineOptions({ name: "VaultFormModal" });
const props = defineProps<VaultFormModalProps>();
const emit = defineEmits<{ "update:open": [open: boolean] }>();
defineSlots<{}>();

const model = reactive({ slug: "", name: "", url: "http://vault:8080" });
const errors = ref<string[]>([]);
const saving = ref(false);
const removing = ref(false);

/** Starts each session from the vault being edited, or blank. */
watch(
  () => props.open,
  (open) => {
    if (!open) return;
    Object.assign(model, {
      slug: props.vault?.slug ?? `${props.server}-vault`,
      name: props.vault?.name ?? "",
      url: props.vault?.url ?? "http://vault:8080",
    });
    errors.value = [];
  },
  { immediate: true },
);

/** The request body the form describes. */
function body(): SaveVaultRequest {
  return {
    ...(props.vault ? {} : { slug: model.slug.trim() }),
    name: model.name.trim(),
    server: props.vault?.server ?? props.server,
    url: model.url.trim(),
  };
}

/** Saves the vault once the API accepts it. */
async function submit(): Promise<void> {
  if (saving.value) return;
  saving.value = true;
  errors.value = [];
  try {
    const result = props.vault ? await props.operations.update(props.vault.slug, body()) : await props.operations.create(body());
    if (!result.ok) errors.value = failureMessages(result.failure);
    else emit("update:open", false);
  } finally {
    saving.value = false;
  }
}

/** Stops administering the vault; the vault and its secrets stay where they run. */
async function remove(): Promise<void> {
  if (!props.vault || removing.value) return;
  removing.value = true;
  errors.value = [];
  try {
    const result = await props.operations.remove(props.vault.slug);
    if (!result.ok) errors.value = failureMessages(result.failure);
    else emit("update:open", false);
  } finally {
    removing.value = false;
  }
}
</script>

<template>
  <Modal :open="open" @update:open="emit('update:open', $event)">
    <ModalContent class="flex max-h-[calc(100dvh-2rem)] w-[min(32rem,calc(100vw-2rem))] flex-col">
      <ModalHeader>
        <ModalTitle>{{ vault ? `Edit ${vault.name}` : "New vault" }}</ModalTitle>
      </ModalHeader>
      <form class="flex min-h-0 flex-1 flex-col" @submit.prevent="submit">
        <ModalBody class="-mx-1 flex min-h-0 flex-1 flex-col gap-4 overflow-y-auto px-1">
          <Field v-if="!vault" label="Slug" helper="Its password goes in vaults/<slug>/password on the control host">
            <TextInput v-model="model.slug" autocomplete="off" />
          </Field>
          <Field label="Name"><TextInput v-model="model.name" autocomplete="off" /></Field>
          <Field label="Endpoint" helper="Reached from the control host, never from the browser">
            <TextInput v-model="model.url" autocomplete="off" />
          </Field>
          <Alert v-if="errors.length" severity="danger" :description="errors.join(' ')" />
        </ModalBody>
        <ModalFooter class="justify-between">
          <DeleteConfirm v-if="vault" label="Remove vault" :is-loading="removing" @confirm="remove" />
          <span v-else />
          <span class="flex gap-2">
            <Button type="button" variant="outline" tone="neutral" @click="emit('update:open', false)">Cancel</Button>
            <Button type="submit" :is-loading="saving">{{ vault ? "Save" : "Add vault" }}</Button>
          </span>
        </ModalFooter>
      </form>
    </ModalContent>
  </Modal>
</template>
