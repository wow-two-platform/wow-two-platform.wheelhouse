<script lang="ts">
/** The new-product form is opened from the route toolbar. */
export interface ProductsPanelProps {
  readonly creating: boolean;
}
</script>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { RouterLink } from "vue-router";
import { ChevronRight, ExternalLink, KeyRound, Package, Pencil, Plus, Search } from "lucide-vue-next";
import { Button, CopyButton } from "@wow-two-beta/ui-vue/presentation/actions";
import { Badge } from "@wow-two-beta/ui-vue/presentation/display";
import {
  Alert,
  SkeletonState,
} from "@wow-two-beta/ui-vue/presentation/feedback";
import {
  SearchInput,
  SelectPicker,
  SelectPickerContent,
  SelectPickerItem,
  SelectPickerTrigger,
  SelectPickerValue,
} from "@wow-two-beta/ui-vue/presentation/forms";
import { useProducts } from "@/application/products";
import { useServers } from "@/application/servers";
import { useTargets } from "@/application/targets";
import { ProductLifecycle } from "@/domain/products";
import type { InventoryTarget } from "@/domain/targets";
import RepositoryActions from "./RepositoryActions.vue";
import ProductIcon from "./ProductIcon.vue";
import ProductFormModal from "./ProductFormModal.vue";
import TargetFormModal from "./TargetFormModal.vue";

/** Presents the portfolio's products with a contextual inspector: their definition, lifecycle and environments. */
defineOptions({ name: "ProductsPanel" });
const props = defineProps<ProductsPanelProps>();
const emit = defineEmits<{ "update:creating": [value: boolean] }>();

/** @internal Each lifecycle's label and badge, in portfolio order. */
const Lifecycles = [
  { value: ProductLifecycle.Idea, label: "Idea", variant: "outline" },
  { value: ProductLifecycle.Building, label: "Building", variant: "info" },
  { value: ProductLifecycle.Live, label: "Live", variant: "success" },
  { value: ProductLifecycle.Paused, label: "Paused", variant: "warning" },
  { value: ProductLifecycle.Killed, label: "Killed", variant: "danger" },
] as const;

const productOperations = useProducts();
const { products, loading, error, reload, setLifecycle } = productOperations;
const servers = useServers();
const editing = ref(false);
const targetForm = ref<{ open: boolean; target: InventoryTarget | null }>({ open: false, target: null });
const search = ref("");
const selectedSlug = ref<string | null>(null);
const saving = ref(false);
const saveError = ref<string | null>(null);
const visibleProducts = computed(() => {
  const term = search.value.trim().toLocaleLowerCase();
  return products.value.filter((product) =>
    `${product.name} ${product.slug} ${product.repository.name}`
      .toLocaleLowerCase()
      .includes(term),
  );
});
const selected = computed(
  () =>
    visibleProducts.value.find((product) => product.slug === selectedSlug.value) ??
    null,
);

/** Keeps the inspector attached to a visible product after searches or catalog changes. */
watch(
  visibleProducts,
  (items) => {
    if (!items.some((item) => item.slug === selectedSlug.value))
      selectedSlug.value = items[0]?.slug ?? null;
  },
  { immediate: true },
);
/** Clears a stale save failure when the inspected product changes. */
watch(selectedSlug, () => {
  saveError.value = null;
});
const targets = useTargets(selectedSlug);
const productForm = computed(() => props.creating || editing.value);
/** Each environment's published sites and vault namespace, from the product's own projection. */
const environmentOf = (target: InventoryTarget) =>
  selected.value?.environments.find((environment) => environment.name === target.environment) ?? null;

/** Closes the product form, whichever opened it. */
function closeProductForm(open: boolean): void {
  if (open) return;
  editing.value = false;
  emit("update:creating", false);
}

/** Looks up a lifecycle's label and badge. @internal */
function lifecycle(value: ProductLifecycle) {
  return Lifecycles.find((item) => item.value === value) ?? Lifecycles[1];
}

/** Records the chosen lifecycle once the server accepts it. */
async function changeLifecycle(value: string | null): Promise<void> {
  const product = selected.value;
  const next = Lifecycles.find((item) => item.value === value)?.value;
  if (!product || !next || next === product.lifecycle || saving.value) return;
  saving.value = true;
  saveError.value = null;
  try {
    const result = await setLifecycle(product.slug, next);
    if (!result.ok) saveError.value = result.failure.message;
  } finally {
    saving.value = false;
  }
}
</script>

<template>
  <div
    class="wh-glass grid min-h-[26rem] overflow-hidden rounded-2xl border lg:grid-cols-[20rem_minmax(0,1fr)]"
  >
    <section
      class="wh-glass-subtle min-w-0 border-b border-border p-4 sm:p-5 lg:border-b-0 lg:border-r"
      aria-label="Product catalog"
    >
      <div class="mb-4 flex items-center justify-between gap-3">
        <h2 class="text-sm font-semibold">Your products</h2>
        <p
          v-if="!loading || products.length > 0"
          class="text-sm text-muted-foreground"
        >
          {{ products.length }}
          {{ products.length === 1 ? "product" : "products" }}
        </p>
        <SkeletonState v-else class="h-4 w-36" aria-hidden="true" />
      </div>
      <SearchInput
        v-model="search"
        aria-label="Search products"
        placeholder="Find a product…"
        class="mb-4 w-full"
      />
      <div v-if="error && products.length > 0" class="mb-4 space-y-3">
        <Alert
          severity="warning"
          title="Showing the last catalog snapshot"
          :description="error"
        />
        <Button size="sm" variant="outline" @click="reload"
          >Retry refresh</Button
        >
      </div>
      <div
        v-if="loading && products.length === 0"
        class="space-y-2"
        role="status"
        aria-label="Loading products"
      >
        <div
          v-for="index in 3"
          :key="index"
          class="flex items-center gap-3 rounded-xl border border-border bg-card p-3"
          aria-hidden="true"
        >
          <SkeletonState class="size-10 shrink-0 rounded-lg" />
          <div class="flex-1 space-y-2">
            <SkeletonState class="h-4 w-3/4" /><SkeletonState
              class="h-3 w-1/2"
            />
          </div>
        </div>
      </div>
      <div v-else-if="error && products.length === 0" class="space-y-3">
        <Alert
          severity="danger"
          title="Catalog unavailable"
          :description="error"
        />
        <Button variant="outline" @click="reload">Retry</Button>
      </div>
      <div
        v-else-if="products.length === 0"
        class="flex min-h-52 flex-col items-center justify-center gap-3 py-6 text-center"
      >
        <div class="rounded-2xl bg-primary-soft p-4 text-primary">
          <Package :size="28" />
        </div>
        <h2 class="text-xl font-semibold">No products yet</h2>
        <Button @click="emit('update:creating', true)">New product</Button>
      </div>
      <div v-else-if="visibleProducts.length === 0" class="py-8 text-center">
        <Search :size="24" class="mx-auto mb-3 text-muted-foreground" />
        <p class="font-medium">No matching products</p>
      </div>
      <div v-else class="max-h-[28rem] space-y-2 overflow-y-auto p-1 -m-1">
        <button
          v-for="product in visibleProducts"
          :key="product.slug"
          type="button"
          :aria-pressed="selectedSlug === product.slug"
          @click="selectedSlug = product.slug"
          class="group flex w-full min-w-0 items-center gap-3 rounded-xl border p-3 text-left transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
          :class="
            selectedSlug === product.slug
              ? 'border-primary/40 bg-primary-soft'
              : 'border-transparent hover:border-border hover:bg-card'
          "
        >
          <ProductIcon :product-slug="product.slug" :name="product.name" />
          <div class="min-w-0 flex-1">
            <p class="truncate text-sm font-semibold">{{ product.name }}</p>
            <p class="mt-1 truncate text-xs text-muted-foreground">
              {{ product.slug }}
            </p>
          </div>
          <Badge size="sm" :variant="lifecycle(product.lifecycle).variant">{{
            lifecycle(product.lifecycle).label
          }}</Badge>
          <ChevronRight :size="16" class="shrink-0 text-muted-foreground" />
        </button>
      </div>
    </section>

    <section aria-label="Product details" class="min-w-0 bg-card p-5 sm:p-7">
      <template v-if="selected">
        <div
          class="flex flex-wrap items-start justify-between gap-4 border-b border-border pb-6"
        >
          <div class="min-w-0">
            <h2 class="flex items-center gap-3 break-words text-xl font-semibold">
              <ProductIcon
                :product-slug="selected.slug"
                :name="selected.name"
                size="lg"
              />
              {{ selected.name }}
            </h2>
            <p class="mt-2 max-w-prose text-sm text-muted-foreground">
              {{ selected.description }}
            </p>
            <p
              class="mt-2 flex items-center gap-1 font-mono text-xs text-muted-foreground"
            >
              {{ selected.slug }}
              <CopyButton
                :text="selected.slug"
                size="sm"
                aria-label="Copy the product slug"
                copied-aria-label="Product slug copied"
              />
            </p>
          </div>
          <div class="flex w-56 items-start gap-2">
            <Button variant="outline" tone="neutral" size="sm" aria-label="Edit product" @click="editing = true">
              <Pencil :size="14" />
            </Button>
            <div class="min-w-0 flex-1">
            <SelectPicker
              :model-value="selected.lifecycle"
              :get-option-label="(value) => lifecycle(value as ProductLifecycle).label"
              :is-disabled="saving"
              @update:model-value="changeLifecycle"
              ><SelectPickerTrigger aria-label="Lifecycle"
                ><SelectPickerValue placeholder="Lifecycle" /></SelectPickerTrigger
              ><SelectPickerContent
                ><SelectPickerItem
                  v-for="item in Lifecycles"
                  :key="item.value"
                  :item-key="item.value"
                  :label="item.label"
                  >{{ item.label }}</SelectPickerItem
                ></SelectPickerContent
              ></SelectPicker
            >
            <p
              v-if="saveError"
              role="alert"
              class="mt-2 text-xs text-destructive"
            >
              {{ saveError }}
            </p>
            </div>
          </div>
        </div>
        <dl class="my-6 grid gap-x-8 gap-y-6 text-sm">
          <div class="min-w-0">
            <dt class="mb-2 text-xs text-muted-foreground">
              Source repository
            </dt>
            <dd>
              <RepositoryActions
                :repository="selected.repository.name"
                :runner-product="selected.slug"
              />
            </dd>
          </div>
          <div class="min-w-0">
            <dt class="mb-2 flex items-center justify-between text-xs text-muted-foreground">
              Environments
              <Button size="sm" variant="ghost" @click="targetForm = { open: true, target: null }">
                <template #leading><Plus :size="14" /></template>Add environment
              </Button>
            </dt>
            <dd v-if="targets.targets.value.length === 0" class="text-muted-foreground">
              No environment yet.
            </dd>
            <dd v-else>
              <ul class="divide-y divide-border rounded-xl border border-border">
                <li
                  v-for="target in targets.targets.value"
                  :key="target.slug"
                  class="grid gap-2 p-3 sm:grid-cols-[6rem_minmax(0,1fr)_minmax(0,12rem)_auto] sm:items-center"
                >
                  <span class="text-sm font-medium" :title="target.slug">{{ target.environment }}</span>
                  <span class="flex min-w-0 flex-wrap gap-x-3 gap-y-1">
                    <a
                      v-for="site in environmentOf(target)?.sites ?? []"
                      :key="site.name"
                      :href="site.url"
                      target="_blank"
                      rel="noopener noreferrer"
                      class="inline-flex min-w-0 items-center gap-1 text-primary hover:underline"
                      :title="site.exposure === 'private' ? 'Private network only' : undefined"
                      ><span class="truncate">{{ site.name }}</span
                      ><ExternalLink :size="12" class="shrink-0"
                    /></a>
                    <span v-if="!(environmentOf(target)?.sites.length)" class="text-muted-foreground"
                      >{{ target.server }} · not rolled out yet</span
                    >
                  </span>
                  <span
                    v-if="environmentOf(target)?.secrets"
                    class="flex min-w-0 items-center gap-1 font-mono text-xs text-muted-foreground"
                    :title="`Vault ${environmentOf(target)?.secrets?.vault}`"
                  >
                    <KeyRound :size="12" class="shrink-0" />
                    <RouterLink to="/secrets" class="truncate hover:text-foreground">{{
                      environmentOf(target)?.secrets?.namespace
                    }}</RouterLink>
                  </span>
                  <span v-else class="text-xs text-muted-foreground">No vault</span>
                  <Button
                    size="sm"
                    variant="ghost"
                    tone="neutral"
                    :aria-label="`Edit ${target.slug}`"
                    @click="targetForm = { open: true, target }"
                  >
                    <Pencil :size="14" />
                  </Button>
                </li>
              </ul>
            </dd>
          </div>
        </dl>
      </template>
      <div
        v-else
        class="flex min-h-60 flex-col items-center justify-center gap-3 text-center text-muted-foreground"
      >
        <Package :size="24" />
        <p class="text-sm">
          {{ search ? "No products match your search." : "Select a product." }}
        </p>
      </div>
    </section>
  </div>

  <ProductFormModal
    :open="productForm"
    :product="props.creating ? null : selected"
    :operations="productOperations"
    @update:open="closeProductForm"
    @saved="(slug) => (selectedSlug = slug)"
  />
  <TargetFormModal
    v-if="selected"
    :open="targetForm.open"
    :product="selected.slug"
    :target="targetForm.target"
    :servers="servers.data.value ?? []"
    :operations="targets"
    @update:open="(open) => (targetForm = { ...targetForm, open })"
  />
</template>
