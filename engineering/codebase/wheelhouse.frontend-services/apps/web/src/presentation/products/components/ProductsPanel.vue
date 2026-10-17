<script lang="ts">
/** The new-product form is opened from the route toolbar. */
export interface ProductsPanelProps {
  readonly creating: boolean;
}
</script>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { RouterLink } from "vue-router";
import { ChevronLeft, ChevronRight, ExternalLink, KeyRound, Package, Pencil, Plus, Search } from "lucide-vue-next";
import { Button, CopyButton } from "@wow-two-beta/fe/presentation/actions";
import { Badge, Text } from "@wow-two-beta/fe/presentation/display";
import { Alert, DataRegion, useSkeletonItems } from "@wow-two-beta/fe/presentation/feedback";
import { SearchInput } from "@wow-two-beta/fe/presentation/forms";
import { ListDetailLayout } from "@wow-two-beta/fe/presentation/layout";
import { IconAvailability, useProducts } from "@/application/products";
import { workspaceOf } from "@/application/portfolio";
import { useWorkspaces } from "@/application/workspaces";
import { failureReason } from "@/integration/common";
import { RefreshButton } from "@/presentation/common/components";
import { useListDetailRoute } from "@/presentation/common/hooks";
import { useServers } from "@/application/servers";
import { useTargets } from "@/application/targets";
import {
  ProductStatusRules,
  homepageLinkOf,
  identityProblemOf,
  serviceIdentitiesOf,
  shortCommitOf,
} from "@/domain/products";
import type { InventoryTarget } from "@/domain/targets";
import RepositoryActions from "./RepositoryActions.vue";
import ProductIcon from "./ProductIcon.vue";
import ProductFormModal from "./ProductFormModal.vue";
import TargetFormModal from "./TargetFormModal.vue";

/** Presents the portfolio's products with a contextual inspector: their definition, status and environments. */
defineOptions({ name: "ProductsPanel" });
const props = defineProps<ProductsPanelProps>();
const emit = defineEmits<{ "update:creating": [value: boolean] }>();

const productOperations = useProducts();
const { products, isFirstLoad, hasData, error, reload } = productOperations;
const servers = useServers();
const workspaces = useWorkspaces();
const editing = ref(false);
const targetForm = ref<{ open: boolean; target: InventoryTarget | null }>({ open: false, target: null });
const search = ref("");
/* `?product=` names the inspected product; on phones it opens the detail. */
const listDetail = useListDetailRoute(["product"]);
const refreshing = ref(false);
const refreshError = ref<string | null>(null);
const visibleProducts = computed(() => {
  const term = search.value.trim().toLocaleLowerCase();
  return products.value.filter((product) =>
    `${product.name} ${product.slug} ${product.repository.name}`
      .toLocaleLowerCase()
      .includes(term),
  );
});
/** The visible products, or placeholder rows until the first catalog arrives. */
const rows = useSkeletonItems(() => (hasData.value ? visibleProducts.value : undefined), 3);
/** The requested product while it is visible; otherwise the first visible one, so a search or catalog change keeps
    the inspector attached. */
const selected = computed(
  () =>
    visibleProducts.value.find((product) => product.slug === listDetail.read("product")) ??
    visibleProducts.value[0] ??
    null,
);
const selectedSlug = computed(() => selected.value?.slug ?? null);
/** Clears a stale refresh failure when the inspected product changes. */
watch(selectedSlug, () => {
  refreshError.value = null;
});
const targets = useTargets(selectedSlug);
const productForm = computed(() => props.creating || editing.value);
/** Each environment's published sites and vault namespace, from the product's own projection. */
const environmentOf = (target: InventoryTarget) =>
  selected.value?.environments.find((environment) => environment.name === target.environment) ?? null;

/** Inspects a product. */
function selectProduct(slug: string): void {
  void listDetail.select({ product: slug });
}

/** Closes the product form, whichever opened it. */
function closeProductForm(open: boolean): void {
  if (open) return;
  editing.value = false;
  emit("update:creating", false);
}

/** Reads the product's manifest again now; a repository GitHub cannot be read for leaves the cached copy and says so. */
async function refreshIdentity(): Promise<void> {
  const product = selected.value;
  if (!product || refreshing.value) return;
  refreshing.value = true;
  refreshError.value = null;
  try {
    const result = await productOperations.refreshIdentity(product.slug);
    if (!result.ok) refreshError.value = failureReason(result.failure);
    // The repository was read again, so an icon it gained shows: its marks ask for it once more.
    else IconAvailability.forget(product.slug);
  } finally {
    refreshing.value = false;
  }
}
</script>

<template>
  <ListDetailLayout
    :is-detail-open="listDetail.isDetailOpen.value"
    back-label="Products"
    back-class="mb-0 bg-card px-3 pt-2"
    class="wh-glass min-h-[26rem] overflow-hidden rounded-2xl border"
    @back="listDetail.back"
  >
    <template #list>
      <section
        class="wh-glass-subtle h-full min-w-0 border-border p-4 sm:p-5 lg:border-r"
        aria-label="Product catalog"
      >
        <div class="mb-4 flex items-center justify-between gap-3">
          <h2 class="text-sm font-semibold">Your products</h2>
          <Text
            class="text-sm text-muted-foreground"
            skeleton-width="5rem"
            v-bind="isFirstLoad ? { isSkeleton: true } : {}"
          >
            <template v-if="hasData"
              >{{ products.length }}
              {{ products.length === 1 ? "product" : "products" }}</template
            >
          </Text>
        </div>
        <SearchInput
          v-model="search"
          aria-label="Search products"
          placeholder="Find a product…"
          class="mb-4 w-full"
        />
        <!-- The catalog has no refresh control, so a failed first load offers its own retry. -->
        <DataRegion
          :is-first-load="isFirstLoad"
          :error="error"
          :has-data="hasData"
          :is-empty="hasData && products.length === 0"
          empty-title="No products yet"
          loading-label="Loading products"
          @retry="reload"
        >
          <template #emptyActions
            ><Button @click="emit('update:creating', true)">New product</Button></template
          >
          <div v-if="hasData && visibleProducts.length === 0" class="py-8 text-center">
            <Search :size="24" class="mx-auto mb-3 text-muted-foreground" />
            <p class="font-medium">No matching products</p>
          </div>
          <div v-else class="-m-1 space-y-2 p-1 lg:max-h-[28rem] lg:overflow-y-auto">
            <button
              v-for="(product, index) in rows"
              :key="product?.slug ?? `placeholder-${index}`"
              type="button"
              :aria-pressed="Boolean(product) && selectedSlug === product?.slug"
              @click="product && selectProduct(product.slug)"
              class="group flex w-full min-w-0 items-center gap-3 rounded-xl border p-3 text-left transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
              :class="
                product && selectedSlug === product.slug
                  ? 'border-primary/40 bg-primary-soft'
                  : 'border-transparent hover:border-border hover:bg-card'
              "
            >
              <ProductIcon :product-slug="product?.slug ?? null" :name="product?.name ?? ''" />
              <div class="min-w-0 flex-1">
                <Text class="truncate text-sm font-semibold text-inherit" skeleton-width="70%">{{ product?.name }}</Text>
                <Text class="mt-1 truncate text-xs text-muted-foreground" skeleton-width="45%">
                  {{ product?.slug }}
                </Text>
              </div>
              <Badge
                size="sm"
                :variant="product && ProductStatusRules.isActive(product) ? 'success' : 'neutral'"
                >{{ product && !ProductStatusRules.isActive(product) ? "Inactive" : "Active" }}</Badge
              >
              <ChevronRight :size="16" class="shrink-0 text-muted-foreground" />
            </button>
          </div>
        </DataRegion>
      </section>
    </template>

    <!-- The pane has no gutter of its own, so the phone back control sits in a bar above the details. -->
    <template #back="{ back, label }">
      <Button variant="ghost" tone="neutral" size="sm" @click="back"
        ><template #leading><ChevronLeft :size="16" /></template>{{ label }}</Button
      >
    </template>
    <template #detail>
      <section aria-label="Product details" class="h-full min-w-0 bg-card p-5 sm:p-7">
        <!-- The first read draws the details' header with placeholder values. -->
        <DataRegion :is-first-load="isFirstLoad" loading-label="Loading the product">
          <div
            v-if="selected || isFirstLoad"
            class="flex flex-wrap items-start justify-between gap-4 border-b border-border pb-6"
          >
            <div class="min-w-0">
              <h2 class="flex items-center gap-3 break-words text-xl font-semibold">
                <ProductIcon
                  :product-slug="selected?.slug ?? null"
                  :name="selected?.name ?? ''"
                  size="lg"
                />
                <Text as="span" class="text-xl font-semibold" skeleton-width="10rem">{{ selected?.name }}</Text>
              </h2>
              <Text class="mt-2 max-w-prose text-sm text-muted-foreground" skeleton-width="18rem">
                {{ selected?.description }}
              </Text>
              <div v-if="selected" class="mt-3 flex flex-wrap items-center gap-2 text-xs">
                <span class="rounded-full border border-border bg-muted/50 px-2.5 py-0.5 font-medium text-muted-foreground">{{
                  workspaceOf(workspaces.workspaces.value, selected)?.name ?? "No workspace"
                }}</span>
                <Badge size="sm" :variant="ProductStatusRules.isActive(selected) ? 'success' : 'neutral'">{{
                  ProductStatusRules.isActive(selected) ? "Active" : "Inactive"
                }}</Badge>
              </div>
              <a
                v-if="selected && homepageLinkOf(selected)"
                :href="homepageLinkOf(selected)?.href"
                target="_blank"
                rel="noopener noreferrer"
                class="mt-2 inline-flex items-center gap-1 text-sm text-primary hover:underline"
                >{{ homepageLinkOf(selected)?.host }}<ExternalLink :size="12" class="shrink-0"
              /></a>
              <Text
                class="mt-2 flex items-center gap-1 font-mono text-xs text-muted-foreground"
                skeleton-width="8rem"
              >
                <template v-if="selected"
                  >{{ selected.slug }}
                  <CopyButton
                    :text="selected.slug"
                    size="sm"
                    aria-label="Copy the product slug"
                    copied-aria-label="Product slug copied"
                /></template>
              </Text>
            </div>
            <div class="flex items-start gap-2">
              <RefreshButton
                icon-only
                label="Refresh from repository"
                :refreshing="refreshing"
                @refresh="refreshIdentity"
              />
              <Button variant="outline" tone="neutral" size="sm" aria-label="Edit product" @click="editing = true">
                <Pencil :size="14" />
              </Button>
            </div>
          </div>
          <template v-if="selected">
            <div v-if="identityProblemOf(selected) || refreshError" class="mt-4 space-y-2">
              <Alert v-if="identityProblemOf(selected)" severity="warning" :description="identityProblemOf(selected) ?? ''" />
              <p v-if="refreshError" role="alert" class="text-xs text-destructive">{{ refreshError }}</p>
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
              <div v-if="serviceIdentitiesOf(selected).length" class="min-w-0">
                <dt class="mb-2 flex items-center justify-between text-xs text-muted-foreground">
                  Services
                  <span
                    v-if="shortCommitOf(selected.identity.commit)"
                    class="font-mono"
                    :title="`${selected.repository.defaultBranch} at ${selected.identity.commit}`"
                    >{{ shortCommitOf(selected.identity.commit) }}</span
                  >
                </dt>
                <dd>
                  <ul class="divide-y divide-border rounded-xl border border-border">
                    <li
                      v-for="service in serviceIdentitiesOf(selected)"
                      :key="service.service"
                      class="flex items-start gap-3 p-3"
                    >
                      <ProductIcon :product-slug="selected.slug" :service="service.service" :name="service.label" size="sm" />
                      <span class="min-w-0 flex-1">
                        <span class="block truncate text-sm font-medium">{{ service.label }}</span>
                        <span v-if="service.description" class="block text-xs text-muted-foreground">{{ service.description }}</span>
                      </span>
                      <span class="font-mono text-xs text-muted-foreground">{{ service.service }}</span>
                    </li>
                  </ul>
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
            v-else-if="!isFirstLoad"
            class="flex min-h-60 flex-col items-center justify-center gap-3 text-center text-muted-foreground"
          >
            <Package :size="24" />
            <p class="text-sm">
              {{ search ? "No products match your search." : "Select a product." }}
            </p>
          </div>
        </DataRegion>
      </section>
    </template>
  </ListDetailLayout>

  <ProductFormModal
    :open="productForm"
    :product="props.creating ? null : selected"
    :operations="productOperations"
    :workspaces="workspaces.workspaces.value"
    @update:open="closeProductForm"
    @saved="selectProduct"
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
