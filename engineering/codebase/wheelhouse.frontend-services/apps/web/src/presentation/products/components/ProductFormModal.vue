<script lang="ts">
import type { ProductOperations } from "@/application/products";
import type { Product } from "@/domain/products";
import type { Workspace } from "@/domain/workspaces";

/** Product creation and editing; the slug is fixed once the product exists. */
export interface ProductFormModalProps {
  readonly open: boolean;
  /** The product to edit, or null to add one. */
  readonly product: Product | null;
  readonly operations: Pick<ProductOperations, "create" | "update" | "remove" | "setStatus" | "moveToWorkspace">;
  /** The workspaces a product can belong to, in custom order. */
  readonly workspaces?: readonly Workspace[];
  /** The workspace a new product starts in; the host's default when absent. */
  readonly defaultWorkspaceId?: string | null;
}
</script>

<script setup lang="ts">
import { computed, ref } from "vue";
import { Button } from "@wow-two-beta/fe/presentation/actions";
import { Alert } from "@wow-two-beta/fe/presentation/feedback";
import {
  Field,
  SelectPicker,
  SelectPickerContent,
  SelectPickerItem,
  SelectPickerTrigger,
  SelectPickerValue,
  SwitchField,
  TextAreaInput,
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
  productFieldPath,
  productFollowUpsOf,
  productFormOf,
  productFormSchema,
  productRequestOf,
} from "@/application/products/ProductForms";
import { identityNoteOf, isIdentityReadOnly } from "@/domain/products";
import { failureReason } from "@/integration/common";
import { DeleteConfirm, RowsField } from "@/presentation/common/components";

/**
 * Edits a product's identity, workspace, status and release source, and removes a product nothing runs any more. The
 * workspace and the status save through their own requests once the product is saved; a refusal there shows as a
 * toast and the saved product stays.
 */
defineOptions({ name: "ProductFormModal" });
const props = withDefaults(defineProps<ProductFormModalProps>(), { workspaces: () => [], defaultWorkspaceId: null });
const emit = defineEmits<{ "update:open": [open: boolean]; saved: [slug: string]; removed: [] }>();
defineSlots<{}>();

const removeError = ref("");
/** Name, description and homepage come from the repository's manifest, so the form shows them without editing. */
const identityReadOnly = computed(() => isIdentityReadOnly(props.product));
const identityNote = computed(() => identityNoteOf(props.product));
const form = useAppForm({
  defaultValues: productFormOf(null),
  schema: productFormSchema,
  mapFieldPath: productFieldPath,
  onSubmit: async (values) => {
    const body = productRequestOf(values, !props.product);
    const result = props.product
      ? await props.operations.update(props.product.slug, body)
      : await props.operations.create(body);
    if (result.ok) {
      const followUps = productFollowUpsOf(values, result.value);
      if (followUps.workspaceId) await props.operations.moveToWorkspace(result.value, followUps.workspaceId);
      if (followUps.status) await props.operations.setStatus(result.value, followUps.status);
      emit("saved", result.value.slug);
      emit("update:open", false);
    }
    return result;
  },
});

/** Starts each session from the product being edited, or blank, once the opening update has set both props. */
useFormDialogSession(form, {
  open: () => props.open,
  initialValues: () => productFormOf(props.product, props.defaultWorkspaceId),
  onOpen: () => (removeError.value = ""),
});

/** Removes the product once the host accepts it; a product an environment still runs stays. */
async function remove(): Promise<void> {
  if (!props.product) return;
  removeError.value = "";
  const result = await props.operations.remove(props.product.slug);
  if (!result.ok) removeError.value = failureReason(result.failure);
  else {
    emit("removed");
    emit("update:open", false);
  }
}
</script>

<template>
  <Modal :open="open" @update:open="emit('update:open', $event)">
    <ModalContent :size="ModalSize.Lg">
      <ModalHeader>
        <ModalTitle>{{ product ? `Edit ${product.name}` : "New product" }}</ModalTitle>
      </ModalHeader>
      <form class="flex min-h-0 flex-1 flex-col" @submit="form.handleSubmit">
        <ModalBody class="flex flex-col gap-4">
          <form.Field v-if="!product" name="slug" is-required v-slot="field">
            <Field label="Slug" helper="Targets, bundles and URLs know it by this; it never changes">
              <TextInput v-model="field.value" autocomplete="off" placeholder="foreverpin" @blur="field.onBlur" />
            </Field>
          </form.Field>
          <p v-if="identityNote" class="text-xs text-muted-foreground">{{ identityNote }}</p>
          <form.Field name="name" is-required v-slot="field">
            <Field label="Name" :is-read-only="identityReadOnly">
              <TextInput v-model="field.value" autocomplete="off" @blur="field.onBlur" />
            </Field>
          </form.Field>
          <form.Field name="description" v-slot="field">
            <Field label="Description" :is-read-only="identityReadOnly">
              <TextAreaInput v-model="field.value" :rows="2" @blur="field.onBlur" />
            </Field>
          </form.Field>
          <Field v-if="product?.homepage" label="Homepage" is-read-only>
            <TextInput :model-value="product.homepage" autocomplete="off" />
          </Field>
          <div class="grid items-end gap-4 sm:grid-cols-[minmax(0,1fr)_auto]">
            <form.Field name="workspaceId" v-slot="field">
              <Field label="Workspace" v-bind="product ? {} : { helper: 'Left empty, it joins the default workspace' }">
                <SelectPicker
                  :model-value="field.value"
                  :get-option-label="(id) => props.workspaces.find((item) => item.id === id)?.name ?? String(id)"
                  @update:model-value="(value: unknown) => field.setValue(typeof value === 'string' ? value : null)"
                  ><SelectPickerTrigger aria-label="Workspace"
                    ><SelectPickerValue placeholder="Choose a workspace" /></SelectPickerTrigger
                  ><SelectPickerContent
                    ><SelectPickerItem
                      v-for="workspace in props.workspaces"
                      :key="workspace.id"
                      :item-key="workspace.id"
                      :label="workspace.name"
                      >{{ workspace.name }}</SelectPickerItem
                    ></SelectPickerContent
                  ></SelectPicker
                >
              </Field>
            </form.Field>
            <form.Field name="active" v-slot="field">
              <SwitchField v-model="field.value" label="Active" wrapper-class-name="pb-2" />
            </form.Field>
          </div>
          <div class="grid gap-4 sm:grid-cols-[minmax(0,1fr)_10rem]">
            <form.Field name="repository" is-required v-slot="field">
              <Field label="Repository">
                <TextInput v-model="field.value" autocomplete="off" placeholder="owner/name" @blur="field.onBlur" />
              </Field>
            </form.Field>
            <form.Field name="defaultBranch" is-required v-slot="field">
              <Field label="Default branch"><TextInput v-model="field.value" autocomplete="off" @blur="field.onBlur" /></Field>
            </form.Field>
          </div>
          <form.Field name="publishes" v-slot="field">
            <SwitchField v-model="field.value" label="Publishes releases" />
          </form.Field>
          <template v-if="form.state.values.publishes">
            <div class="grid gap-4 sm:grid-cols-2">
              <form.Field name="asset" is-required v-slot="field">
                <Field label="Release asset">
                  <TextInput v-model="field.value" autocomplete="off" placeholder="product-release.tar.gz" @blur="field.onBlur" />
                </Field>
              </form.Field>
              <form.Field name="workflow" v-slot="field">
                <Field label="Build workflow">
                  <TextInput v-model="field.value" autocomplete="off" placeholder="publish-docker-image.yml" @blur="field.onBlur" />
                </Field>
              </form.Field>
            </div>
            <RowsField
              :form="form"
              name="images"
              label="Images"
              add-label="Add service"
              :blank="{ service: '', image: '' }"
              :columns="[
                { key: 'service', label: 'Service', placeholder: 'api' },
                { key: 'image', label: 'Image', placeholder: 'ghcr.io/owner/product/api' },
              ]"
            />
          </template>
          <Alert
            v-if="removeError || form.state.submitError"
            severity="danger"
            :description="removeError || failureReason(form.state.submitError)"
          />
        </ModalBody>
        <ModalFooter class="sm:justify-between">
          <DeleteConfirm v-if="product" label="Delete product" :question="`Delete ${product.name}?`" :on-confirm="remove" />
          <span v-else class="max-sm:hidden" />
          <span class="flex flex-col-reverse gap-2 sm:flex-row">
            <Button type="button" variant="outline" tone="neutral" @click="emit('update:open', false)">Cancel</Button>
            <Button type="submit" :is-loading="form.state.isSubmitting">{{ product ? "Save" : "Add product" }}</Button>
          </span>
        </ModalFooter>
      </form>
    </ModalContent>
  </Modal>
</template>
