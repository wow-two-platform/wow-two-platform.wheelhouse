<script lang="ts">
import type { PlacementOperations } from "@/application/infrastructure";
import type { Product } from "@/domain/products";
import type { Server } from "@/domain/servers";

/** Places a product on a server: its dev and prod databases, its site nodes, or both. */
export interface PlacementModalProps {
  readonly open: boolean;
  readonly product: Product;
  /** The inventory's servers; the local rig is left out. */
  readonly servers: readonly Server[];
  readonly operations: PlacementOperations;
}
</script>

<script setup lang="ts">
import { computed } from "vue";
import { Plus, X } from "lucide-vue-next";
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
  TextInput,
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
import { useFieldArray, useFormDialogSession } from "@wow-two-beta/fe/forms-engine";
import { useAppForm } from "@/bootstrap/form";
import {
  placementFormOf,
  placementFormSchema,
  placementRequestOf,
} from "@/application/infrastructure/PlacementForms";
import { MAX_PLACED_SITES, PlacementRules, type PlacementSite } from "@/domain/infrastructure";
import { failureReason } from "@/integration/common";

/**
 * Chooses the server (any but the local rig), whether to create the dev and prod databases, and the sites to place:
 * each under a node name that starts as `<product>-<site>` and follows the site until the owner types their own. Prod's
 * node is the name itself, dev's adds `-dev`. Placing queues the job; the Hosting panel follows it live.
 */
defineOptions({ name: "PlacementModal" });
const props = defineProps<PlacementModalProps>();
const emit = defineEmits<{ "update:open": [open: boolean] }>();
defineSlots<{}>();

const eligible = computed(() => PlacementRules.eligibleServers(props.servers));
const form = useAppForm({
  defaultValues: placementFormOf(props.product, props.servers),
  schema: placementFormSchema,
  onSubmit: async (values) => {
    const result = await props.operations.place(props.product.slug, placementRequestOf(values));
    if (result.ok) emit("update:open", false);
    return result;
  },
});
const rows = useFieldArray<PlacementSite>(form, "sites");

useFormDialogSession(form, {
  open: () => props.open,
  initialValues: () => placementFormOf(props.product, props.servers),
});

/** Changes a row's site; a node still at the old site's default follows the new one. @internal */
function changeSite(index: number, value: string): void {
  const row = form.state.values.sites[index];
  form.setValue(`sites[${index}].site`, value);
  form.setValue(`sites[${index}].node`, PlacementRules.followSite(props.product.slug, row?.site ?? "", value, row?.node ?? ""));
}
</script>

<template>
  <Modal :open="open" @update:open="emit('update:open', $event)">
    <ModalContent :size="ModalSize.Lg">
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
          <form.Field name="database" v-slot="field">
            <CheckboxField
              :model-value="field.value"
              label="Create dev and prod databases"
              @update:model-value="field.setValue($event === true)"
            />
          </form.Field>
          <form.Field name="sites" v-slot="list">
            <fieldset class="flex flex-col gap-2">
              <legend class="mb-1 text-sm font-medium">Sites</legend>
              <div
                v-if="rows.length"
                class="grid grid-cols-[minmax(0,1fr)_minmax(0,1.4fr)_auto] gap-2 text-xs text-muted-foreground"
                aria-hidden="true"
              >
                <span>Site</span><span>Node</span><span class="w-10 max-md:w-11" />
              </div>
              <div
                v-for="row in rows.rows"
                :key="row.key"
                class="grid grid-cols-[minmax(0,1fr)_minmax(0,1.4fr)_auto] items-start gap-2"
              >
                <rows.Field :index="row.index" name="site" v-slot="cell">
                  <Field>
                    <TextInput
                      :model-value="cell.value"
                      :aria-label="`Site ${row.index + 1}`"
                      autocomplete="off"
                      autocapitalize="off"
                      placeholder="app"
                      @update:model-value="changeSite(row.index, String($event ?? ''))"
                      @blur="cell.onBlur"
                    />
                  </Field>
                </rows.Field>
                <rows.Field :index="row.index" name="node" v-slot="cell">
                  <Field>
                    <TextInput
                      v-model="cell.value"
                      :aria-label="`Node ${row.index + 1}`"
                      autocomplete="off"
                      autocapitalize="off"
                      :placeholder="PlacementRules.defaultNode(product.slug, form.state.values.sites[row.index]?.site || 'app')"
                      @blur="cell.onBlur"
                    />
                  </Field>
                </rows.Field>
                <Button
                  type="button"
                  variant="ghost"
                  tone="neutral"
                  size="md"
                  shape="square"
                  class="max-md:size-11"
                  :aria-label="`Remove site ${row.index + 1}`"
                  @click="rows.remove(row.index)"
                >
                  <X :size="14" />
                </Button>
              </div>
              <p v-for="message in list.errors" :key="message" role="alert" class="text-sm text-destructive">{{ message }}</p>
              <div class="flex flex-wrap items-center gap-3">
                <Button
                  type="button"
                  variant="outline"
                  tone="neutral"
                  size="sm"
                  class="max-md:min-h-11"
                  :is-disabled="rows.length >= MAX_PLACED_SITES"
                  @click="rows.push({ site: '', node: '' })"
                >
                  <template #leading><Plus :size="14" /></template>Add site
                </Button>
                <span class="text-xs text-muted-foreground">Prod's node is &lt;node&gt;; dev's is &lt;node&gt;-dev.</span>
              </div>
            </fieldset>
          </form.Field>
          <Alert v-if="form.state.submitError" severity="danger" :description="failureReason(form.state.submitError)" />
        </ModalBody>
        <ModalFooter>
          <Button type="button" variant="outline" tone="neutral" @click="emit('update:open', false)">Cancel</Button>
          <Button type="submit" :is-disabled="!eligible.length" :is-loading="form.state.isSubmitting">Place</Button>
        </ModalFooter>
      </form>
    </ModalContent>
  </Modal>
</template>
