<script lang="ts">
/** The sidebar's Products destination and, on a product page, the trail to that product. */
export interface ProductsNavItemProps {
  /** The icon rail: the destination, then the workspace and the product as icons. */
  readonly collapsed?: boolean;
}
</script>
<script setup lang="ts">
import { computed } from "vue";
import { RouterLink, useRoute } from "vue-router";
import { Boxes, Layers } from "lucide-vue-next";
import { SidebarMenuItem } from "@wow-two-beta/fe/presentation/nav";
import { ConsolePaths, primaryDestinationOf, productTrailOf, workspaceLocation } from "@/application/navigation";
import { usePortfolio } from "@/application/portfolio/usePortfolio";
import { ProductStatusRules } from "@/domain/products";
import { ProductIcon } from "@/presentation/products";
import { useNavConditions } from "../hooks/useNavConditions";
import { useProductsTarget } from "../hooks/useProductsTarget";
import ProductStatusMark from "./ProductStatusMark.vue";

/**
 * Products, and while a product page is open its Workspace › Product beneath it, as a breadcrumb: the workspace opens
 * the Products page at that workspace's group, and the product row is the current place. The row itself leads back to
 * the product page last opened (and its tab) from outside Products, and up to the grid from inside it. The rail keeps
 * the same order with the workspace and the product as icons.
 */
defineOptions({ name: "ProductsNavItem" });
const props = withDefaults(defineProps<ProductsNavItemProps>(), { collapsed: false });
const emit = defineEmits<{ navigate: [] }>();
defineSlots<{}>();
const route = useRoute();
const portfolio = usePortfolio();
const products = useProductsTarget();
const conditions = useNavConditions();

const isProducts = computed(() => primaryDestinationOf(route.path) === ConsolePaths.products);
const trail = computed(() => {
  const value = route.params["product"];
  return productTrailOf(
    typeof value === "string" ? value : null,
    portfolio.products.products.value,
    portfolio.workspaces.workspaces.value,
  );
});
const productsLabel = computed(() =>
  products.rememberedProduct.value && !isProducts.value ? `Products — back to ${products.rememberedProduct.value.name}` : "Products",
);
const isDimmed = computed(() => Boolean(trail.value?.product && !ProductStatusRules.isActive(trail.value.product)));
</script>
<template>
  <!-- The rail: the destination, then the open product's workspace and the product itself. -->
  <template v-if="props.collapsed">
    <SidebarMenuItem as-child :is-active="isProducts && !trail">
      <template #icon><Boxes :size="16" /></template>
      <RouterLink :to="products.target.value" :title="productsLabel" @click="emit('navigate')"
        ><span class="sr-only">{{ productsLabel }}</span></RouterLink
      >
    </SidebarMenuItem>
    <!-- Grouped on a quiet ground, so the two read as Products' own rows rather than more destinations. -->
    <li v-if="trail" class="flex">
      <ul role="list" aria-label="Open product" class="flex w-full flex-col gap-0.5 rounded-lg bg-muted/70 py-0.5">
        <SidebarMenuItem v-if="trail.workspace" as-child>
          <template #icon><Layers :size="15" /></template>
          <RouterLink
            :to="workspaceLocation(trail.workspace.slug)"
            :title="`${trail.workspace.name}: all its products`"
            @click="emit('navigate')"
            ><span class="sr-only">{{ trail.workspace.name }}</span></RouterLink
          >
        </SidebarMenuItem>
        <SidebarMenuItem as-child is-active>
          <template #icon
            ><span class="relative inline-flex"
              ><ProductIcon :product-slug="trail.slug" :name="trail.name" size="xs" /><ProductStatusMark
                :condition="conditions.get(trail.slug)"
                is-badge /></span
          ></template>
          <RouterLink :to="ConsolePaths.product(trail.slug)" :title="trail.name" @click="emit('navigate')"
            ><span class="sr-only">{{ trail.name }}</span></RouterLink
          >
        </SidebarMenuItem>
      </ul>
    </li>
  </template>

  <li v-else class="flex flex-col">
    <ul role="list">
      <!-- On a product page the product row is the current place; on the grid, Products is. -->
      <SidebarMenuItem as-child :is-active="isProducts && !trail">
        <template #icon><Boxes :size="16" /></template>
        <RouterLink :to="products.target.value" :title="productsLabel" @click="emit('navigate')"
          ><span class="min-w-0 flex-1 truncate text-start">Products</span></RouterLink
        >
      </SidebarMenuItem>
    </ul>
    <ul
      v-if="trail"
      role="list"
      aria-label="Open product"
      class="ms-4 flex flex-col gap-0.5 border-s border-border ps-2 pt-0.5"
    >
      <SidebarMenuItem v-if="trail.workspace" as-child>
        <template #icon><Layers :size="15" /></template>
        <RouterLink
          :to="workspaceLocation(trail.workspace.slug)"
          :title="`${trail.workspace.name}: all its products`"
          class="h-8 text-[13px]"
          @click="emit('navigate')"
          ><span class="min-w-0 flex-1 truncate text-start">{{ trail.workspace.name }}</span></RouterLink
        >
      </SidebarMenuItem>
      <li class="flex flex-col">
        <ul role="list" :class="trail.workspace ? 'ms-3 border-s border-border ps-2' : ''">
          <SidebarMenuItem as-child is-active>
            <template #icon><ProductIcon :product-slug="trail.slug" :name="trail.name" size="xs" /></template>
            <template #trailing><ProductStatusMark :condition="conditions.get(trail.slug)" /></template>
            <RouterLink
              :to="ConsolePaths.product(trail.slug)"
              :class="isDimmed ? 'opacity-60' : ''"
              :title="trail.name"
              @click="emit('navigate')"
              ><span class="min-w-0 flex-1 truncate text-start">{{ trail.name }}</span></RouterLink
            >
          </SidebarMenuItem>
        </ul>
      </li>
    </ul>
  </li>
</template>
