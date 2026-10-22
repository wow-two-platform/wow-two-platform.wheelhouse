<script lang="ts">
import type { TargetOperations } from "@/application/targets";
import type { KnownSite } from "@/application/targets/TargetForms";
import type { Placement } from "@/domain/infrastructure";
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
  /** The sites the product declares; the form lists each, and a public one answers on its baseline host. */
  readonly knownSites?: readonly KnownSite[];
  /** The product's placements, which name a private site's tailnet node. */
  readonly placements?: readonly Pick<Placement, "serverId" | "facts">[];
  /** The environments the product already has a target for; a new environment starts as the first it lacks. */
  readonly takenEnvironments?: readonly string[];
  /** The services the product's deploy.yml declares; the environment runs every one of them on its server. */
  readonly services?: readonly string[];
}
</script>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { Button } from "@wow-two-beta/fe/presentation/actions";
import { Badge } from "@wow-two-beta/fe/presentation/display";
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
  ModalSize,
} from "@wow-two-beta/fe/presentation/overlays";
import { useFormDialogSession } from "@wow-two-beta/fe/forms-engine";
import { useAppForm } from "@/bootstrap/form";
import {
  firstFreeEnvironment,
  publicAddressOf,
  siteHostsOf,
  siteRowsOf,
  storedHostsOf,
  suggestedTargetSlug,
  tailnetNodeOf,
  targetFormOf,
  targetFormSchema,
  targetRequestOf,
} from "@/application/targets/TargetForms";
import { EnvironmentOrder, type DeploymentEnvironment } from "@/domain/targets";
import type { SiteHostRow } from "@/application/targets/TargetForms";
import { failureReason } from "@/integration/common";
import { DeleteConfirm, RowsField } from "@/presentation/common/components";

/** Edits where an environment runs, which settings files its services read and which requests prove a rollout.
 * Shows, read-only, what the product declares: the services deploy.yml names (the environment runs each on the chosen
 * server) and its sites, one row each with the host it answers on. The host is the target's stored one, else a public
 * site's baseline host on the chosen server's public address, else a private site's tailnet node; nothing is typed, and
 * the derived hosts follow the environment and the server. A new environment starts as the first one the product
 * lacks, dev then prod. */
defineOptions({ name: "TargetFormModal" });
const props = withDefaults(defineProps<TargetFormModalProps>(), {
  knownSites: () => [],
  placements: () => [],
  takenEnvironments: () => [],
  services: () => [],
});
const emit = defineEmits<{ "update:open": [open: boolean] }>();
defineSlots<{}>();

const environments = EnvironmentOrder;
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

/** The site rows for an environment on a server: the product's sites with the host each answers on. @internal */
function rowsFor(environment: DeploymentEnvironment, server: string): SiteHostRow[] {
  return siteRowsOf(props.knownSites, storedHostsOf(props.target, environment, server), {
    product: props.product,
    environment,
    address: publicAddressOf(props.servers, server),
    node: (site) => tailnetNodeOf(props.product, site, server, environment, props.placements),
  });
}

/** Starts each session from the target being edited, or the first environment the product lacks on the first server,
 * once the opening update has set every prop. The sites are the hosts the rows show. */
useFormDialogSession(form, {
  open: () => props.open,
  initialValues: () => {
    const environment = props.target?.environment ?? firstFreeEnvironment(props.takenEnvironments);
    const values = targetFormOf(props.target, props.product, props.servers[0]?.slug ?? null, environment);
    return { ...values, sites: siteHostsOf(rowsFor(values.environment, values.server)) };
  },
  onOpen: () => (removeError.value = ""),
});

/** What the Site hosts show, and the form sends. */
const siteRows = computed(() => rowsFor(form.state.values.environment, form.state.values.server));

/** Keeps the sent hosts as the rows show them: they follow the environment, the server, the sites deploy.yml declares
 * once it is read and the nodes a placement registers. */
watch(siteRows, (rows) => {
  if (!props.open) return;
  const sites = siteHostsOf(rows);
  if (JSON.stringify(sites) !== JSON.stringify(form.state.values.sites)) form.setValue("sites", sites);
});

/** Keeps a new target's slug following its environment until the owner types another. */
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
    <ModalContent :size="ModalSize.Lg">
      <ModalHeader>
        <ModalTitle>{{ target ? `Edit ${target.slug}` : "New environment" }}</ModalTitle>
      </ModalHeader>
      <form class="flex min-h-0 flex-1 flex-col" @submit="form.handleSubmit">
        <ModalBody class="flex flex-col gap-4">
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
          <div v-if="props.services.length" class="flex flex-col gap-1.5">
            <span id="target-services-label" class="text-sm font-medium">Services</span>
            <ul role="list" aria-labelledby="target-services-label" class="flex flex-wrap gap-1.5">
              <li v-for="service in props.services" :key="service"><Badge variant="outline" size="sm">{{ service }}</Badge></li>
            </ul>
            <span class="text-xs text-muted-foreground">From deploy.yml: each one runs on the chosen server</span>
          </div>
          <form.Field v-if="!target" name="slug" is-required v-slot="field">
            <Field label="Slug" helper="Deployments know the environment by this; it never changes">
              <TextInput v-model="field.value" autocomplete="off" @blur="field.onBlur" />
            </Field>
          </form.Field>
          <!-- Every server's kit lays out the same network and folder, so Wheelhouse fills both. The target keeps the
               folder; each environment deploys into its own `<folder>/<product>-<environment>/`. -->
          <div class="grid gap-4 sm:grid-cols-2">
            <form.Field name="network" v-slot="field">
              <Field label="Network" is-read-only helper="Set by the server kit"
                ><TextInput v-model="field.value" autocomplete="off"
              /></Field>
            </form.Field>
            <form.Field name="root" v-slot="field">
              <Field label="Deploy folder" is-read-only helper="Each environment gets its own folder inside"
                ><TextInput v-model="field.value" autocomplete="off"
              /></Field>
            </form.Field>
          </div>
          <RowsField
            :form="form"
            name="settings"
            label="Host settings files (optional)"
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
          <!-- The product declares its sites and Wheelhouse derives their hosts (decisions 47, 68), so nothing is typed. -->
          <fieldset class="flex flex-col gap-2">
            <legend class="mb-1 text-sm font-medium">Site hosts</legend>
            <div
              v-if="siteRows.length"
              class="grid gap-2 text-xs text-muted-foreground max-sm:hidden sm:grid-cols-[minmax(0,1fr)_minmax(0,2fr)]"
              aria-hidden="true"
            >
              <span>Site</span><span>Host</span>
            </div>
            <div
              v-for="row in siteRows"
              :key="row.site"
              class="grid items-start gap-2 sm:grid-cols-[minmax(0,1fr)_minmax(0,2fr)]"
            >
              <Field is-read-only
                ><TextInput :model-value="row.site" :aria-label="`Site ${row.site}`" autocomplete="off"
              /></Field>
              <Field is-read-only :helper="row.note ?? ''"
                ><TextInput
                  :model-value="row.host || row.node || ''"
                  :aria-label="`Host of ${row.site}`"
                  placeholder="Not set yet"
                  autocomplete="off"
              /></Field>
            </div>
            <p v-if="!siteRows.length" class="text-sm text-muted-foreground">No sites known yet</p>
          </fieldset>
          <Alert
            v-if="removeError || form.state.submitError"
            severity="danger"
            :description="removeError || failureReason(form.state.submitError)"
          />
        </ModalBody>
        <ModalFooter class="sm:justify-between">
          <DeleteConfirm
            v-if="target"
            label="Remove environment"
            :question="`Remove ${target.slug}?`"
            :on-confirm="remove"
          />
          <span v-else class="max-sm:hidden" />
          <span class="flex flex-col-reverse gap-2 sm:flex-row">
            <Button type="button" variant="outline" tone="neutral" @click="emit('update:open', false)">Cancel</Button>
            <Button type="submit" :is-loading="form.state.isSubmitting">{{ target ? "Save" : "Add environment" }}</Button>
          </span>
        </ModalFooter>
      </form>
    </ModalContent>
  </Modal>
</template>
