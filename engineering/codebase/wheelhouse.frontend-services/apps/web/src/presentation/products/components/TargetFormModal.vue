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
import { computed, reactive, ref, watch } from "vue";
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
import { DeploymentEnvironment, type SaveTargetRequest } from "@/domain/targets";
import { failureMessages } from "@/integration/common";
import { DeleteConfirm, RowsEditor } from "@/presentation/common/components";

/** Edits where an environment runs, which settings files its services read, which requests prove a rollout and
 * which hosts its sites answer on. */
defineOptions({ name: "TargetFormModal" });
const props = defineProps<TargetFormModalProps>();
const emit = defineEmits<{ "update:open": [open: boolean] }>();
defineSlots<{}>();

const model = reactive({
  slug: "",
  environment: DeploymentEnvironment.Dev as DeploymentEnvironment,
  server: null as string | null,
  network: "platform",
  root: "/srv/wheelhouse",
  settings: [] as Record<string, string | number>[],
  smokeChecks: [] as Record<string, string | number>[],
  sites: [] as Record<string, string | number>[],
});
const slugEdited = ref(false);
const errors = ref<string[]>([]);
const saving = ref(false);
const removing = ref(false);
const environments = Object.values(DeploymentEnvironment);
const suggestedSlug = computed(() => `${props.product}-${model.environment}`);

/** Starts each session from the target being edited, or a dev environment on the first server. */
watch(
  () => props.open,
  (open) => {
    if (!open) return;
    const target = props.target;
    Object.assign(model, {
      slug: target?.slug ?? `${props.product}-dev`,
      environment: target?.environment ?? DeploymentEnvironment.Dev,
      server: target?.server ?? props.servers[0]?.slug ?? null,
      network: target?.network ?? "platform",
      root: target?.root ?? "/srv/wheelhouse",
      settings: target?.settings.map((setting) => ({ ...setting })) ?? [],
      smokeChecks: target?.smokeChecks.map((check) => ({ ...check })) ?? [],
      sites: target?.sites.map((site) => ({ ...site })) ?? [],
    });
    slugEdited.value = false;
    errors.value = [];
  },
  { immediate: true },
);

/** Keeps a new target's slug following its environment until the operator types one. */
watch(suggestedSlug, (slug) => {
  if (!props.target && !slugEdited.value) model.slug = slug;
});

/** The request body the form describes. */
function body(): SaveTargetRequest {
  return {
    ...(props.target ? {} : { slug: model.slug.trim() }),
    product: props.product,
    server: model.server ?? "",
    environment: model.environment,
    network: model.network.trim(),
    root: model.root.trim(),
    settings: model.settings.map((row) => ({ service: String(row.service).trim(), path: String(row.path).trim() })),
    smokeChecks: model.smokeChecks.map((row) => ({
      service: String(row.service).trim(), path: String(row.path).trim(), status: Number(row.status),
    })),
    sites: model.sites.map((row) => ({ site: String(row.site).trim(), host: String(row.host).trim() })),
  };
}

/** Saves the environment once the server accepts it. */
async function submit(): Promise<void> {
  if (saving.value) return;
  saving.value = true;
  errors.value = [];
  try {
    const result = props.target
      ? await props.operations.update(props.target.slug, body())
      : await props.operations.create(body());
    if (!result.ok) errors.value = failureMessages(result.failure);
    else emit("update:open", false);
  } finally {
    saving.value = false;
  }
}

/** Removes the environment from the inventory; what runs on its host stays until torn down there. */
async function remove(): Promise<void> {
  if (!props.target || removing.value) return;
  removing.value = true;
  errors.value = [];
  try {
    const result = await props.operations.remove(props.target.slug);
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
        <ModalTitle>{{ target ? `Edit ${target.slug}` : "New environment" }}</ModalTitle>
      </ModalHeader>
      <form class="flex min-h-0 flex-1 flex-col" @submit.prevent="submit">
        <ModalBody class="-mx-1 flex min-h-0 flex-1 flex-col gap-4 overflow-y-auto px-1">
          <div class="grid gap-4 sm:grid-cols-2">
            <Field label="Environment">
              <SelectPicker v-model="model.environment"
                ><SelectPickerTrigger aria-label="Environment"><SelectPickerValue /></SelectPickerTrigger
                ><SelectPickerContent
                  ><SelectPickerItem v-for="value in environments" :key="value" :item-key="value" :label="value"
                /></SelectPickerContent
              ></SelectPicker>
            </Field>
            <Field label="Server">
              <SelectPicker v-model="model.server"
                ><SelectPickerTrigger aria-label="Server"><SelectPickerValue placeholder="Choose a server" /></SelectPickerTrigger
                ><SelectPickerContent
                  ><SelectPickerItem
                    v-for="server in servers"
                    :key="server.slug"
                    :item-key="server.slug"
                    :label="server.name"
                /></SelectPickerContent
              ></SelectPicker>
            </Field>
          </div>
          <Field v-if="!target" label="Slug" helper="Deployments know the environment by this; it never changes">
            <TextInput v-model="model.slug" autocomplete="off" @update:model-value="slugEdited = true" />
          </Field>
          <div class="grid gap-4 sm:grid-cols-2">
            <Field label="Network"><TextInput v-model="model.network" autocomplete="off" /></Field>
            <Field label="Releases folder"><TextInput v-model="model.root" autocomplete="off" /></Field>
          </div>
          <RowsEditor
            v-model="model.settings"
            label="Settings files"
            add-label="Add service"
            :columns="[
              { key: 'service', label: 'Service', placeholder: 'api' },
              { key: 'path', label: 'Path on the host', placeholder: '/srv/settings/product/api.json' },
            ]"
          />
          <RowsEditor
            v-model="model.smokeChecks"
            label="Smoke checks"
            add-label="Add check"
            :columns="[
              { key: 'service', label: 'Service', placeholder: 'api' },
              { key: 'path', label: 'Path', placeholder: '/health' },
              { key: 'status', label: 'Status', initial: 200 },
            ]"
          />
          <RowsEditor
            v-model="model.sites"
            label="Site hosts"
            add-label="Add site"
            :columns="[
              { key: 'site', label: 'Site', placeholder: 'app' },
              { key: 'host', label: 'Host', placeholder: 'app.example.com' },
            ]"
          />
          <Alert v-if="errors.length" severity="danger" :description="errors.join(' ')" />
        </ModalBody>
        <ModalFooter class="justify-between">
          <DeleteConfirm v-if="target" label="Remove environment" :is-loading="removing" @confirm="remove" />
          <span v-else />
          <span class="flex gap-2">
            <Button type="button" variant="outline" tone="neutral" @click="emit('update:open', false)">Cancel</Button>
            <Button type="submit" :is-loading="saving">{{ target ? "Save" : "Add environment" }}</Button>
          </span>
        </ModalFooter>
      </form>
    </ModalContent>
  </Modal>
</template>
