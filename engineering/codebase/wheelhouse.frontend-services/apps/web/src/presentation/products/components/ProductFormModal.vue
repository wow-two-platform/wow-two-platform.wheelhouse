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
import { reactive, ref, watch } from "vue";
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
import type { SaveProductRequest } from "@/domain/products";
import { failureMessages } from "@/integration/common";
import { DeleteConfirm, RowsEditor } from "@/presentation/common/components";

/** Edits a product's identity and release source, and removes a product nothing runs any more. */
defineOptions({ name: "ProductFormModal" });
const props = defineProps<ProductFormModalProps>();
const emit = defineEmits<{ "update:open": [open: boolean]; saved: [slug: string]; removed: [] }>();
defineSlots<{}>();

const model = reactive({
  slug: "",
  name: "",
  description: "",
  repository: "",
  defaultBranch: "main",
  publishes: false,
  asset: "",
  workflow: "",
  images: [] as Record<string, string | number>[],
});
const errors = ref<string[]>([]);
const saving = ref(false);
const removing = ref(false);

/** Starts each session from the product being edited, or blank. */
watch(
  () => props.open,
  (open) => {
    if (!open) return;
    const product = props.product;
    Object.assign(model, {
      slug: product?.slug ?? "",
      name: product?.name ?? "",
      description: product?.description ?? "",
      repository: product?.repository.name ?? "",
      defaultBranch: product?.repository.defaultBranch ?? "main",
      publishes: product?.release != null,
      asset: product?.release?.asset ?? "",
      workflow: product?.release?.workflow ?? "",
      images: product?.release?.images.map((image) => ({ ...image })) ?? [],
    });
    errors.value = [];
  },
  { immediate: true },
);

/** The request body the form describes. */
function body(): SaveProductRequest {
  return {
    ...(props.product ? {} : { slug: model.slug.trim() }),
    name: model.name.trim(),
    description: model.description.trim(),
    repository: model.repository.trim(),
    defaultBranch: model.defaultBranch.trim(),
    release: model.publishes
      ? {
          asset: model.asset.trim(),
          workflow: model.workflow.trim() || null,
          images: model.images.map((row) => ({ service: String(row.service).trim(), image: String(row.image).trim() })),
        }
      : null,
  };
}

/** Saves the product once the server accepts it. */
async function submit(): Promise<void> {
  if (saving.value) return;
  saving.value = true;
  errors.value = [];
  try {
    const result = props.product
      ? await props.operations.update(props.product.slug, body())
      : await props.operations.create(body());
    if (!result.ok) errors.value = failureMessages(result.failure);
    else {
      emit("saved", result.value.slug);
      emit("update:open", false);
    }
  } finally {
    saving.value = false;
  }
}

/** Removes the product once the server accepts it; a product a target runs stays. */
async function remove(): Promise<void> {
  if (!props.product || removing.value) return;
  removing.value = true;
  errors.value = [];
  try {
    const result = await props.operations.remove(props.product.slug);
    if (!result.ok) errors.value = failureMessages(result.failure);
    else {
      emit("removed");
      emit("update:open", false);
    }
  } finally {
    removing.value = false;
  }
}
</script>

<template>
  <Modal :open="open" @update:open="emit('update:open', $event)">
    <ModalContent class="flex max-h-[calc(100dvh-2rem)] w-[min(40rem,calc(100vw-2rem))] flex-col">
      <ModalHeader>
        <ModalTitle>{{ product ? `Edit ${product.name}` : "New product" }}</ModalTitle>
      </ModalHeader>
      <form class="flex min-h-0 flex-1 flex-col" @submit.prevent="submit">
        <ModalBody class="-mx-1 flex min-h-0 flex-1 flex-col gap-4 overflow-y-auto px-1">
          <Field v-if="!product" label="Slug" helper="Targets, bundles and URLs know it by this; it never changes">
            <TextInput v-model="model.slug" autocomplete="off" placeholder="foreverpin" />
          </Field>
          <Field label="Name"><TextInput v-model="model.name" autocomplete="off" /></Field>
          <Field label="Description"><TextAreaInput v-model="model.description" :rows="2" /></Field>
          <div class="grid gap-4 sm:grid-cols-[minmax(0,1fr)_10rem]">
            <Field label="Repository"><TextInput v-model="model.repository" autocomplete="off" placeholder="owner/name" /></Field>
            <Field label="Default branch"><TextInput v-model="model.defaultBranch" autocomplete="off" /></Field>
          </div>
          <SwitchField v-model="model.publishes" label="Publishes releases" />
          <template v-if="model.publishes">
            <div class="grid gap-4 sm:grid-cols-2">
              <Field label="Release asset"><TextInput v-model="model.asset" autocomplete="off" placeholder="product-release.tar.gz" /></Field>
              <Field label="Build workflow"><TextInput v-model="model.workflow" autocomplete="off" placeholder="publish-docker-image.yml" /></Field>
            </div>
            <RowsEditor
              v-model="model.images"
              label="Images"
              add-label="Add service"
              :columns="[
                { key: 'service', label: 'Service', placeholder: 'api' },
                { key: 'image', label: 'Image', placeholder: 'ghcr.io/owner/product/api' },
              ]"
            />
          </template>
          <Alert v-if="errors.length" severity="danger" :description="errors.join(' ')" />
        </ModalBody>
        <ModalFooter class="justify-between">
          <DeleteConfirm v-if="product" label="Delete product" :is-loading="removing" @confirm="remove" />
          <span v-else />
          <span class="flex gap-2">
            <Button type="button" variant="outline" tone="neutral" @click="emit('update:open', false)">Cancel</Button>
            <Button type="submit" :is-loading="saving">{{ product ? "Save" : "Add product" }}</Button>
          </span>
        </ModalFooter>
      </form>
    </ModalContent>
  </Modal>
</template>
