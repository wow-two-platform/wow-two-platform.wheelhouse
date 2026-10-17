<script lang="ts">
import type { InventoryProduct } from "@/application/product/ProductInventory";
import type { Workspace } from "@/domain/workspaces";

/** The product page's header, above its tabs. */
export interface ProductHeaderProps {
  /** The product with its targets; null draws the header's frame during the first read. */
  readonly product: InventoryProduct | null;
  readonly workspace: Workspace | null;
}
</script>
<script setup lang="ts">
import { computed, ref, useTemplateRef } from "vue";
import { RouterLink } from "vue-router";
import { ExternalLink, Layers } from "lucide-vue-next";
import { Badge, Text } from "@wow-two-beta/fe/presentation/display";
import { Alert } from "@wow-two-beta/fe/presentation/feedback";
import { SwitchField } from "@wow-two-beta/fe/presentation/forms";
import { ConsolePaths } from "@/application/navigation";
import { useProducts } from "@/application/products";
import { PinKind } from "@/domain/pins";
import { ProductStatus, ProductStatusRules, homepageLinkOf, identityProblemOf } from "@/domain/products";
import { PinToggle } from "@/presentation/pins";
import { ProductIcon } from "@/presentation/products";
import RepositoryActions from "@/presentation/products/components/RepositoryActions.vue";
import ProductManageMenu from "./ProductManageMenu.vue";

/**
 * Names the product — mark, name, description, workspace and homepage — and holds what applies to every tab: its pin,
 * the Active switch, its repository and the Manage menu for its definition and environments. A manifest the console
 * cannot use, or a refresh that failed, shows under it.
 */
defineOptions({ name: "ProductHeader" });
const props = defineProps<ProductHeaderProps>();
defineSlots<{}>();
const products = useProducts();
const manage = useTemplateRef<{ addEnvironment(): void }>("manage");
const refreshError = ref<string | null>(null);
const record = computed(() => props.product?.product ?? null);
const homepage = computed(() => homepageLinkOf(record.value));
const problem = computed(() => identityProblemOf(record.value));

/** Marks the product active or inactive; the change shows at once and rolls back if the host refuses it. */
function setActive(value: boolean): void {
  if (record.value) void products.setStatus(record.value, value ? ProductStatus.Active : ProductStatus.Inactive);
}

defineExpose({ addEnvironment: () => manage.value?.addEnvironment() });
</script>
<template>
  <header class="wh-glass rounded-2xl border border-border p-5 sm:p-6">
    <div class="flex flex-wrap items-start justify-between gap-4">
      <div class="flex min-w-0 items-start gap-4">
        <ProductIcon
          :product-slug="record ? props.product?.key : null"
          :name="record?.name ?? props.product?.key ?? ''"
          size="lg"
        />
        <div class="min-w-0">
          <h2 class="flex flex-wrap items-center gap-x-3 gap-y-1 text-2xl font-semibold tracking-tight">
            <Text as="span" class="text-2xl font-semibold text-inherit" skeleton-width="10rem">{{
              props.product ? (record?.name ?? props.product.key) : ""
            }}</Text>
            <Badge v-if="record && !ProductStatusRules.isActive(record)" variant="neutral" size="sm">Inactive</Badge>
          </h2>
          <Text class="mt-1 line-clamp-2 text-sm text-muted-foreground" skeleton-width="16rem">{{
            props.product ? record?.description || (record ? "" : "Not in the product catalog") : ""
          }}</Text>
          <div class="mt-3 flex flex-wrap items-center gap-2">
            <RouterLink
              v-if="props.workspace"
              :to="ConsolePaths.workspace(props.workspace.slug)"
              class="inline-flex items-center gap-1.5 rounded-full border border-border bg-muted/50 px-2.5 py-0.5 text-xs font-medium text-muted-foreground hover:border-primary/40 hover:text-foreground focus-visible:outline-2 focus-visible:outline-ring"
              ><Layers :size="12" />{{ props.workspace.name }}</RouterLink
            >
            <Text v-else-if="!props.product" as="span" class="text-xs" skeleton-width="5rem" />
            <a
              v-if="homepage"
              :href="homepage.href"
              target="_blank"
              rel="noopener noreferrer"
              class="inline-flex items-center gap-1 rounded-full px-1 text-xs font-medium text-primary hover:underline focus-visible:outline-2 focus-visible:outline-ring"
              >{{ homepage.host }}<ExternalLink :size="12" class="shrink-0"
            /></a>
          </div>
        </div>
      </div>
      <div v-if="record" class="flex flex-wrap items-center gap-x-4 gap-y-2 text-sm">
        <PinToggle :subject="{ kind: PinKind.Product, product: record }" :label="record.name" show-text />
        <SwitchField
          :model-value="ProductStatusRules.isActive(record)"
          size="sm"
          label="Active"
          @update:model-value="setActive"
        />
        <RepositoryActions :repository="record.repository.name" :runner-product="props.product?.key ?? null" />
        <ProductManageMenu ref="manage" :product="record" @refresh-failed="refreshError = $event" />
      </div>
    </div>
    <div v-if="problem || refreshError" class="mt-4 space-y-2">
      <Alert v-if="problem" severity="warning" :description="problem" />
      <p v-if="refreshError" role="alert" class="text-xs text-destructive">{{ refreshError }}</p>
    </div>
  </header>
</template>
