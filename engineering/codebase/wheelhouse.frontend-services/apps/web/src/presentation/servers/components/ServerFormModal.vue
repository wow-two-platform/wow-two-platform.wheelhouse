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
import { reactive, ref, watch } from "vue";
import { Button } from "@wow-two-beta/ui-vue/presentation/actions";
import { Alert } from "@wow-two-beta/ui-vue/presentation/feedback";
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
} from "@wow-two-beta/ui-vue/presentation/forms";
import {
  Modal,
  ModalBody,
  ModalContent,
  ModalFooter,
  ModalHeader,
  ModalTitle,
} from "@wow-two-beta/ui-vue/presentation/overlays";
import { VpsProvider, VpsProviderLabels, type SaveServerRequest } from "@/domain/servers";
import { failureMessages } from "@/integration/common";
import { DeleteConfirm } from "@/presentation/common/components";

/** Edits how Wheelhouse reaches a server and how its ingress publishes sites. The SSH identity and pinned host key
 * stay files on the control host, so a new server reaches nothing until the operator places them. */
defineOptions({ name: "ServerFormModal" });
const props = defineProps<ServerFormModalProps>();
const emit = defineEmits<{ "update:open": [open: boolean] }>();
defineSlots<{}>();

const model = reactive({
  slug: "",
  name: "",
  provider: VpsProvider.Hetzner as VpsProvider,
  host: "",
  region: "",
  sshUser: "deploy",
  sshPort: 22 as number | null,
  scheme: "https" as "http" | "https",
  port: null as number | null,
  entryPoints: ["websecure"] as string[],
  privateEntryPoints: [] as string[],
  certResolver: "letsencrypt",
  pattern: "",
  probe: "",
  privateProbe: "",
});
const errors = ref<string[]>([]);
const saving = ref(false);
const removing = ref(false);

/** Starts each session from the server being edited, or a Hetzner default. */
watch(
  () => props.open,
  (open) => {
    if (!open) return;
    const server = props.server;
    Object.assign(model, {
      slug: server?.slug ?? "",
      name: server?.name ?? "",
      provider: server?.provider ?? VpsProvider.Hetzner,
      host: server?.host ?? "",
      region: server?.region ?? "",
      sshUser: server?.sshUser ?? "deploy",
      sshPort: server?.sshPort ?? 22,
      scheme: server?.ingress.scheme ?? "https",
      port: server?.ingress.port ?? null,
      entryPoints: [...(server?.ingress.entryPoints ?? ["websecure"])],
      privateEntryPoints: [...(server?.ingress.privateEntryPoints ?? [])],
      certResolver: server ? (server.ingress.certResolver ?? "") : "letsencrypt",
      pattern: server?.ingress.pattern ?? "",
      probe: server?.ingress.probe ?? "",
      privateProbe: server?.ingress.privateProbe ?? "",
    });
    errors.value = [];
  },
  { immediate: true },
);

/** Empty optional text means absent. */
function optional(value: string): string | null {
  return value.trim() || null;
}

/** The request body the form describes. */
function body(): SaveServerRequest {
  return {
    ...(props.server ? {} : { slug: model.slug.trim() }),
    name: model.name.trim(),
    provider: model.provider,
    host: model.host.trim(),
    region: model.region.trim(),
    sshUser: model.sshUser.trim(),
    sshPort: model.sshPort ?? 22,
    ingress: {
      scheme: model.scheme,
      port: model.port,
      entryPoints: model.entryPoints,
      privateEntryPoints: model.privateEntryPoints,
      certResolver: optional(model.certResolver),
      pattern: optional(model.pattern),
      probe: optional(model.probe),
      privateProbe: optional(model.privateProbe),
    },
  };
}

/** Saves the server once the API accepts it. */
async function submit(): Promise<void> {
  if (saving.value) return;
  saving.value = true;
  errors.value = [];
  try {
    const result = props.server
      ? await props.operations.update(props.server.slug, body())
      : await props.operations.create(body());
    if (!result.ok) errors.value = failureMessages(result.failure);
    else emit("update:open", false);
  } finally {
    saving.value = false;
  }
}

/** Removes the server once nothing runs on it. */
async function remove(): Promise<void> {
  if (!props.server || removing.value) return;
  removing.value = true;
  errors.value = [];
  try {
    const result = await props.operations.remove(props.server.slug);
    if (!result.ok) errors.value = failureMessages(result.failure);
    else emit("update:open", false);
  } finally {
    removing.value = false;
  }
}
</script>

<template>
  <Modal :open="open" @update:open="emit('update:open', $event)">
    <ModalContent class="flex max-h-[calc(100dvh-2rem)] w-[min(44rem,calc(100vw-2rem))] flex-col">
      <ModalHeader>
        <ModalTitle>{{ server ? `Edit ${server.name}` : "New server" }}</ModalTitle>
      </ModalHeader>
      <form class="flex min-h-0 flex-1 flex-col" @submit.prevent="submit">
        <ModalBody class="-mx-1 flex min-h-0 flex-1 flex-col gap-4 overflow-y-auto px-1">
          <div class="grid gap-4 sm:grid-cols-2">
            <Field v-if="!server" label="Slug" helper="Its SSH identity goes in ssh/<slug>/ on the control host">
              <TextInput v-model="model.slug" autocomplete="off" placeholder="hel1" />
            </Field>
            <Field label="Name"><TextInput v-model="model.name" autocomplete="off" /></Field>
            <Field label="Provider">
              <SelectPicker v-model="model.provider" :get-option-label="(value) => VpsProviderLabels[value as VpsProvider]"
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
            <Field label="Region"><TextInput v-model="model.region" autocomplete="off" placeholder="hel1" /></Field>
          </div>
          <div class="grid gap-4 sm:grid-cols-[minmax(0,1fr)_9rem_7rem]">
            <Field label="Host"><TextInput v-model="model.host" autocomplete="off" placeholder="vps.example.net" /></Field>
            <Field label="SSH user"><TextInput v-model="model.sshUser" autocomplete="off" /></Field>
            <Field label="SSH port"><NumberInput v-model="model.sshPort" /></Field>
          </div>
          <fieldset class="flex flex-col gap-4">
            <legend class="mb-1 text-sm font-medium">Ingress</legend>
            <div class="grid gap-4 sm:grid-cols-3">
              <Field label="Scheme">
                <SelectPicker v-model="model.scheme"
                  ><SelectPickerTrigger aria-label="Scheme"><SelectPickerValue /></SelectPickerTrigger
                  ><SelectPickerContent
                    ><SelectPickerItem item-key="https" label="https" /><SelectPickerItem item-key="http" label="http"
                  /></SelectPickerContent
                ></SelectPicker>
              </Field>
              <Field label="Port"><NumberInput v-model="model.port" /></Field>
              <Field label="Certificate resolver"><TextInput v-model="model.certResolver" autocomplete="off" /></Field>
            </div>
            <div class="grid gap-4 sm:grid-cols-2">
              <Field label="Entry points"><TagsInput v-model="model.entryPoints" placeholder="websecure" /></Field>
              <Field label="Private entry points"><TagsInput v-model="model.privateEntryPoints" /></Field>
            </div>
            <Field label="Host pattern" helper="For sites a target leaves unnamed; prod on a VPS names its own">
              <TextInput v-model="model.pattern" autocomplete="off" placeholder="{site}-{product}.{environment}.preview.example" />
            </Field>
            <div class="grid gap-4 sm:grid-cols-2">
              <Field label="Probe"><TextInput v-model="model.probe" autocomplete="off" placeholder="http://ingress:80" /></Field>
              <Field label="Private probe"><TextInput v-model="model.privateProbe" autocomplete="off" /></Field>
            </div>
          </fieldset>
          <Alert v-if="errors.length" severity="danger" :description="errors.join(' ')" />
        </ModalBody>
        <ModalFooter class="justify-between">
          <DeleteConfirm v-if="server" label="Delete server" :is-loading="removing" @confirm="remove" />
          <span v-else />
          <span class="flex gap-2">
            <Button type="button" variant="outline" tone="neutral" @click="emit('update:open', false)">Cancel</Button>
            <Button type="submit" :is-loading="saving">{{ server ? "Save" : "Add server" }}</Button>
          </span>
        </ModalFooter>
      </form>
    </ModalContent>
  </Modal>
</template>
