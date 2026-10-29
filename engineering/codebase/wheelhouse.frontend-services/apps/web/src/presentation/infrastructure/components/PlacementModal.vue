<script lang="ts">
import type { PlacementOperations } from "@/application/infrastructure";
import type { Placement, PlacementPlan } from "@/domain/infrastructure";
import type { Product } from "@/domain/products";
import type { Server } from "@/domain/servers";

/** Places a product on a server: what its deploy.yml calls for — its dev and prod databases, its private sites' nodes. */
export interface PlacementModalProps {
  readonly open: boolean;
  readonly product: Product;
  /** What the product's deploy.yml calls for; the dialog offers nothing else. */
  readonly plan: PlacementPlan;
  /** The inventory's servers; the local rig is left out. */
  readonly servers: readonly Server[];
  /** The product's earlier placements: a site keeps the node a host already registered for it. */
  readonly placements?: readonly Placement[];
  readonly operations: PlacementOperations;
}
</script>

<script setup lang="ts">
import { computed } from "vue";
import { pluralize } from "@wow-two-beta/fe/foundation/formatters";
import { Button } from "@wow-two-beta/fe/presentation/actions";
import { Alert } from "@wow-two-beta/fe/presentation/feedback";
import {
  CheckboxField,
  Field,
  SelectPicker,
  SelectPickerContent,
  SelectPickerItem,
  SelectPickerTrigger,
  SelectPickerValue,
} from "@wow-two-beta/fe/presentation/forms";
import {
  Modal,
  ModalBody,
  ModalContent,
  ModalFooter,
  ModalHeader,
  ModalSize,
  ModalTitle,
} from "@wow-two-beta/fe/presentation/overlays";
import { useFormDialogSession } from "@wow-two-beta/fe/forms-engine";
import { useAppForm } from "@/bootstrap/form";
import {
  PlacementForms,
  placementFormOf,
  placementFormSchema,
  placementRequestOf,
} from "@/application/infrastructure/PlacementForms";
import { PlacementRules } from "@/domain/infrastructure";
import { failureReason } from "@/integration/common";

/**
 * Offers only what the product's deploy.yml calls for, all ticked: the dev and prod databases when a service needs
 * PostgreSQL, and each private site under its node — `<product>-<site>`, or the node the server already holds for it —
 * shown read-only, prod's as named and dev's with `-dev`. Public sites need nothing: they answer on the server's
 * baseline hosts. Place stays off until a server and something to place are chosen. Placing queues the job; the
 * Hosting panel follows it live.
 */
defineOptions({ name: "PlacementModal" });
const props = withDefaults(defineProps<PlacementModalProps>(), { placements: () => [] });
const emit = defineEmits<{ "update:open": [open: boolean] }>();
defineSlots<{}>();

const eligible = computed(() => PlacementRules.eligibleServers(props.servers));
const form = useAppForm({
  defaultValues: placementFormOf(props.plan, props.servers),
  schema: placementFormSchema,
  onSubmit: async (values) => {
    const request = placementRequestOf(values, props.plan, (site) => nodeOf(site, values.server));
    const result = await props.operations.place(props.product.slug, request);
    if (result.ok) emit("update:open", false);
    return result;
  },
});

useFormDialogSession(form, {
  open: () => props.open,
  initialValues: () => placementFormOf(props.plan, props.servers),
});

const canPlace = computed(() => Boolean(form.state.values.server) && PlacementForms.hasChoice(form.state.values));
/** Why the public sites are missing from the list. @internal */
const publicNote = computed(() => {
  const sites = props.plan.publicSites;
  if (!sites.length) return "";
  return sites.length === 1
    ? `${sites[0]} is public: it answers on the server's baseline host, no node needed.`
    : `${sites.join(", ")} are public: they answer on the server's baseline hosts, no nodes needed.`;
});

/** A site's prod node on a server. @internal */
function nodeOf(site: string, server: string): string {
  return PlacementRules.nodeFor(props.product.slug, site, server, props.placements);
}
</script>

<template>
  <Modal :open="open" @update:open="emit('update:open', $event)">
    <ModalContent :size="ModalSize.Md">
      <ModalHeader>
        <ModalTitle>Place {{ product.name }} on a server</ModalTitle>
      </ModalHeader>
      <form class="flex min-h-0 flex-1 flex-col" @submit="form.handleSubmit">
        <ModalBody class="flex flex-col gap-4">
          <form.Field name="server" is-required v-slot="field">
            <Field label="Server">
              <SelectPicker v-model="field.value"
                ><SelectPickerTrigger aria-label="Server"><SelectPickerValue placeholder="Choose a server" /></SelectPickerTrigger
                ><SelectPickerContent
                  ><SelectPickerItem
                    v-for="server in eligible"
                    :key="server.slug"
                    :item-key="server.slug"
                    :label="server.name" /></SelectPickerContent
              ></SelectPicker>
            </Field>
          </form.Field>
          <p v-if="!eligible.length" class="text-sm text-muted-foreground">Add a server first; the local rig keeps its own layout.</p>
          <form.Field v-if="plan.databases.length" name="database" v-slot="field">
            <CheckboxField
              :model-value="field.value"
              label="Create dev and prod databases"
              :description="`${plan.databases.join(', ')} ${pluralize(plan.databases.length, 'needs', 'need')} PostgreSQL`"
              @update:model-value="field.setValue($event === true)"
            />
          </form.Field>
          <form.Field v-if="plan.sites.length" name="sites" v-slot="field">
            <fieldset class="flex flex-col gap-2">
              <legend class="mb-1 text-sm font-medium">Private sites</legend>
              <CheckboxField
                v-for="site in plan.sites"
                :key="site"
                :model-value="field.value.includes(site)"
                :label="site"
                @update:model-value="field.setValue(PlacementForms.toggled(plan, field.value, site, $event === true))"
              >
                <template #description
                  ><span class="font-mono">{{ nodeOf(site, form.state.values.server) }}</span> prod ·
                  <span class="font-mono">{{ PlacementRules.devNode(nodeOf(site, form.state.values.server)) }}</span>
                  dev</template
                >
              </CheckboxField>
            </fieldset>
          </form.Field>
          <p v-if="publicNote" class="text-xs text-muted-foreground">{{ publicNote }}</p>
          <Alert v-if="form.state.submitError" severity="danger" :description="failureReason(form.state.submitError)" />
        </ModalBody>
        <ModalFooter>
          <Button type="button" variant="outline" tone="neutral" @click="emit('update:open', false)">Cancel</Button>
          <Button type="submit" :is-disabled="!canPlace" :is-loading="form.state.isSubmitting">Place</Button>
        </ModalFooter>
      </form>
    </ModalContent>
  </Modal>
</template>
