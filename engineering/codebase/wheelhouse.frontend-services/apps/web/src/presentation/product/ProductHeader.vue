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
import { Layers } from "lucide-vue-next";
import { Badge, Text } from "@wow-two-beta/fe/presentation/display";
import { Alert } from "@wow-two-beta/fe/presentation/feedback";
import { ConsolePaths } from "@/application/navigation";
import { PinKind } from "@/domain/pins";
import { ProductStatusRules, identityNoticeOf } from "@/domain/products";
import { useIsPhone } from "@/presentation/deployments/hooks/useIsPhone";
import { PinToggle } from "@/presentation/pins";
import { ProductIcon } from "@/presentation/products";
import OpenSiteMenu from "@/presentation/products/components/OpenSiteMenu.vue";
import ProductManageMenu from "./ProductManageMenu.vue";

/**
 * Names the product and holds what applies to every tab, kept apart: the identity block — mark, name (with an
 * Inactive badge), a two-line description and its workspace chip, which opens the workspace's group on the Products
 * page — and one action row of the same height: Open site (one address opens directly, several list each site with
 * dev and prod), the pin, and Manage (status, edit, environments, repository and clone). On a phone the mark shrinks,
 * the actions turn to icons beside the name and the description keeps two lines, so the tabs stay above the fold. A
 * manifest the console cannot use, or a refresh that failed, shows under it.
 */
defineOptions({ name: "ProductHeader" });
const props = defineProps<ProductHeaderProps>();
defineSlots<{}>();
const isPhone = useIsPhone();
const manage = useTemplateRef<{ addEnvironment(): void }>("manage");
const refreshError = ref<string | null>(null);
const record = computed(() => props.product?.product ?? null);
const notice = computed(() => identityNoticeOf(record.value));

defineExpose({ addEnvironment: () => manage.value?.addEnvironment() });
</script>
<template>
  <header class="wh-glass rounded-2xl border border-border p-4 sm:p-5">
    <div class="flex min-w-0 items-start gap-3 sm:gap-4">
      <ProductIcon
        :product-slug="record ? props.product?.key : null"
        :name="record?.name ?? props.product?.key ?? ''"
        size="lg"
        class="max-sm:size-10! max-sm:rounded-lg!"
      />
      <div class="flex min-w-0 flex-1 flex-col gap-1">
        <div class="flex min-w-0 items-start gap-3">
          <h2 class="flex min-w-0 flex-1 items-center gap-2 pt-0.5 sm:pt-1">
            <Text as="span" class="truncate text-xl font-semibold tracking-tight text-inherit sm:text-2xl" skeleton-width="10rem">{{
              props.product ? (record?.name ?? props.product.key) : ""
            }}</Text>
            <Badge v-if="record && !ProductStatusRules.isActive(record)" variant="neutral" size="sm" class="shrink-0"
              >Inactive</Badge
            >
          </h2>
          <div v-if="record" class="-me-1 flex shrink-0 items-center gap-1.5 sm:me-0" aria-label="Product actions" role="group">
            <OpenSiteMenu :product="record" variant="outline" :show-text="!isPhone" />
            <PinToggle :subject="{ kind: PinKind.Product, product: record }" :label="record.name" variant="outline" />
            <ProductManageMenu
              ref="manage"
              :product="record"
              :runner-product="props.product?.key ?? null"
              @refresh-failed="refreshError = $event"
            />
          </div>
        </div>
        <Text class="line-clamp-2 text-sm text-muted-foreground" skeleton-width="16rem">{{
          props.product ? record?.description || (record ? "" : "Not in the product catalog") : ""
        }}</Text>
        <div v-if="props.workspace || !props.product" class="mt-1 flex flex-wrap items-center gap-2">
          <RouterLink
            v-if="props.workspace"
            :to="ConsolePaths.workspace(props.workspace.slug)"
            class="inline-flex items-center gap-1.5 rounded-full border border-border bg-muted/50 px-2.5 py-0.5 text-xs font-medium text-muted-foreground hover:border-primary/40 hover:text-foreground focus-visible:outline-2 focus-visible:outline-ring max-md:min-h-8"
            ><Layers :size="12" aria-hidden="true" />{{ props.workspace.name }}</RouterLink
          >
          <Text v-else as="span" class="text-xs" skeleton-width="5rem" />
        </div>
      </div>
    </div>
    <div v-if="notice || refreshError" class="mt-4 space-y-2">
      <Alert v-if="notice" severity="warning" :description="notice" />
      <p v-if="refreshError" role="alert" class="text-xs text-destructive">{{ refreshError }}</p>
    </div>
  </header>
</template>
