<script lang="ts">
import type { ProductOperations } from "@/application/products";
import type { Product } from "@/domain/products";

/** Product creation and editing; the slug is fixed once the product exists. */
export interface ProductFormModalProps {
  readonly open: boolean;
  /** The product to edit, or null to add one. */
  readonly product: Product | null;
  readonly operations: Pick<ProductOperations, "create" | "update" | "remove">;
}
</script>

<script setup lang="ts">
import { ref, watch } from "vue";
import { Button } from "@wow-two-beta/ui-vue/presentation/actions";
import { Alert } from "@wow-two-beta/ui-vue/presentation/feedback";
import { Field, SwitchField, TextAreaInput, TextInput } from "@wow-two-beta/ui-vue/presentation/forms";
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
  productFieldPath,
  productFormOf,
  productFormSchema,
  productRequestOf,
} from "@/application/products/ProductForms";
import { failureReason } from "@/integration/common";
import { DeleteConfirm, RowsField } from "@/presentation/common/components";

/** Edits a product's identity and release source, and removes a product nothing runs any more. */
defineOptions({ name: "ProductFormModal" });
const props = defineProps<ProductFormModalProps>();
const emit = defineEmits<{ "update:open": [open: boolean]; saved: [slug: string]; removed: [] }>();
defineSlots<{}>();

const removeError = ref("");
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
      emit("saved", result.value.slug);
      emit("update:open", false);
    }
    return result;
  },
});

/** Starts each session from the product being edited, or blank. */
watch(
  () => props.open,
  (open) => {
    if (!open) return;
    removeError.value = "";
    form.invalidateSession(productFormOf(props.product));
  },
  { immediate: true, flush: "sync" },
);

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
    <ModalContent class="flex max-h-[calc(100dvh-2rem)] w-[min(40rem,calc(100vw-2rem))] flex-col">
      <ModalHeader>
        <ModalTitle>{{ product ? `Edit ${product.name}` : "New product" }}</ModalTitle>
      </ModalHeader>
      <form class="flex min-h-0 flex-1 flex-col" @submit="form.handleSubmit">
        <ModalBody class="-mx-1 flex min-h-0 flex-1 flex-col gap-4 overflow-y-auto px-1">
          <form.Field v-if="!product" name="slug" is-required v-slot="field">
            <Field label="Slug" helper="Targets, bundles and URLs know it by this; it never changes">
              <TextInput v-model="field.value" autocomplete="off" placeholder="foreverpin" @blur="field.onBlur" />
            </Field>
          </form.Field>
          <form.Field name="name" is-required v-slot="field">
            <Field label="Name"><TextInput v-model="field.value" autocomplete="off" @blur="field.onBlur" /></Field>
          </form.Field>
          <form.Field name="description" v-slot="field">
            <Field label="Description"><TextAreaInput v-model="field.value" :rows="2" @blur="field.onBlur" /></Field>
          </form.Field>
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
        <ModalFooter class="justify-between">
          <DeleteConfirm v-if="product" label="Delete product" :question="`Delete ${product.name}?`" :on-confirm="remove" />
          <span v-else />
          <span class="flex gap-2">
            <Button type="button" variant="outline" tone="neutral" @click="emit('update:open', false)">Cancel</Button>
            <Button type="submit" :is-loading="form.state.isSubmitting">{{ product ? "Save" : "Add product" }}</Button>
          </span>
        </ModalFooter>
      </form>
    </ModalContent>
  </Modal>
</template>
