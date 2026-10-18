<script lang="ts">
/** The sidebar's Products destination and the products it keeps one click away. */
export interface ProductsNavTreeProps {
  /** The icon rail: the destination, then each kept product as an icon with its status badge. */
  readonly collapsed?: boolean;
}
</script>
<script setup lang="ts">
import { computed, defineAsyncComponent, reactive, ref } from "vue";
import { RouterLink, useRoute } from "vue-router";
import { Boxes, Check, ChevronRight, LayoutGrid, Pencil } from "lucide-vue-next";
import { SkeletonState } from "@wow-two-beta/fe/presentation/feedback";
import { SidebarMenuItem } from "@wow-two-beta/fe/presentation/nav";
import { ConsolePaths, primaryDestinationOf } from "@/application/navigation";
import { usePinned } from "@/application/pins";
import { usePortfolioSummary } from "@/application/portfolio/usePortfolioSummary";
import { useSidebarProductsOpen } from "@/application/preferences";
import { rolloutConditionOf, type ProductCondition } from "@/domain/portfolio";
import type { Product } from "@/domain/products";
import { ProductIcon } from "@/presentation/products";
import { useProductsTarget } from "../hooks/useProductsTarget";
import ProductNavNode from "./ProductNavNode.vue";
import ProductStatusMark from "./ProductStatusMark.vue";

/** The editor loads when reordering starts; it draws on the SDK's sortable list, which browsing never needs. @internal */
const PinEditor = defineAsyncComponent(() => import("@/presentation/pins/PinEditor.vue"));

/**
 * Products as a tree: the row leads back to the product last opened (or up to the grid from inside Products), its
 * chevron folds the tree, remembered. Inside: "All products" (the grid), the product last opened when it is not
 * pinned, then the pinned products in the owner's order — each with its mark, its name, a status mark only when its
 * latest rollouts need the owner, and its tabs one unfold away; the current product unfolds by itself. Status comes
 * from the latest rollouts alone, so the always-visible sidebar never asks the hosts for vitals. Reorder edits the
 * pins in place. The rail shows the destination, then each of those products as an icon with its status badge.
 */
defineOptions({ name: "ProductsNavTree" });
const props = withDefaults(defineProps<ProductsNavTreeProps>(), { collapsed: false });
const emit = defineEmits<{ navigate: [] }>();
defineSlots<{}>();
const route = useRoute();
const open = useSidebarProductsOpen();
const pinned = usePinned();
const summary = usePortfolioSummary();
const products = useProductsTarget();
const editing = ref(false);
/** Products the owner unfolded or folded by hand this visit; the current product unfolds unless folded. @internal */
const toggled = reactive(new Map<string, boolean>());

const isProducts = computed(() => primaryDestinationOf(route.path) === ConsolePaths.products);
const conditions = computed(
  () => new Map<string, ProductCondition>((summary.data.value?.products ?? []).map((item) => [item.product, rolloutConditionOf(item)])),
);
/** The pinned products that still exist, in the owner's order. @internal */
const pinnedProducts = computed(() =>
  pinned.visible.value.flatMap((entry) => (entry.product ? [entry.product] : [])),
);
/** The product last opened, unless it is pinned and so listed anyway. @internal */
const recent = computed<Product | null>(() => {
  const product = products.rememberedProduct.value;
  return product && !pinnedProducts.value.some((item) => item.slug === product.slug) ? product : null;
});
const nodes = computed(() => [
  ...(recent.value ? [{ product: recent.value, isRecent: true }] : []),
  ...pinnedProducts.value.map((product) => ({ product, isRecent: false })),
]);
const isEditing = computed(() => editing.value && !props.collapsed);
const productsLabel = computed(() =>
  products.rememberedProduct.value && !isProducts.value ? `Products — back to ${products.rememberedProduct.value.name}` : "Products",
);

function isExpanded(product: Product): boolean {
  return toggled.get(product.slug) ?? route.params["product"] === product.slug;
}

function toggle(product: Product): void {
  toggled.set(product.slug, !isExpanded(product));
}
</script>
<template>
  <!-- The rail: the destination, then each kept product as an icon. -->
  <template v-if="props.collapsed">
    <SidebarMenuItem as-child :is-active="isProducts">
      <template #icon><Boxes :size="16" /></template>
      <RouterLink :to="products.target.value" :title="productsLabel" @click="emit('navigate')"
        ><span class="sr-only">{{ productsLabel }}</span></RouterLink
      >
    </SidebarMenuItem>
    <SidebarMenuItem
      v-for="node in nodes"
      :key="node.product.slug"
      as-child
      :is-active="route.params['product'] === node.product.slug"
    >
      <template #icon
        ><span class="relative inline-flex"
          ><ProductIcon :product-slug="node.product.slug" :name="node.product.name" size="xs" /><ProductStatusMark
            :condition="conditions.get(node.product.slug)"
            is-badge /></span
      ></template>
      <RouterLink
        :to="ConsolePaths.product(node.product.slug)"
        :title="node.isRecent ? `${node.product.name} (last opened)` : node.product.name"
        @click="emit('navigate')"
        ><span class="sr-only">{{ node.product.name }}</span></RouterLink
      >
    </SidebarMenuItem>
  </template>

  <li v-else class="flex flex-col">
    <div class="flex items-center gap-0.5">
      <ul role="list" class="min-w-0 flex-1">
        <!-- Unfolded, the tree's own row carries the selection; folded, Products does. -->
        <SidebarMenuItem as-child :is-active="isProducts && !open">
          <template #icon><Boxes :size="16" /></template>
          <RouterLink :to="products.target.value" :title="productsLabel" @click="emit('navigate')"
            ><span class="min-w-0 flex-1 truncate text-start">Products</span></RouterLink
          >
        </SidebarMenuItem>
      </ul>
      <button
        type="button"
        class="inline-flex size-7 shrink-0 items-center justify-center rounded-md text-muted-foreground transition-colors hover:bg-muted hover:text-foreground focus-visible:outline-2 focus-visible:outline-ring"
        :aria-expanded="open"
        aria-controls="products-nav-tree"
        :aria-label="`${open ? 'Hide' : 'Show'} the kept products`"
        @click="open = !open"
      >
        <ChevronRight
          :size="14"
          class="transition-transform duration-150 motion-reduce:transition-none"
          :class="open ? 'rotate-90' : ''"
        />
      </button>
    </div>
    <ul
      v-show="open"
      id="products-nav-tree"
      role="list"
      aria-label="Products"
      class="ms-4 flex flex-col gap-0.5 border-s border-border ps-2 pt-0.5"
    >
      <SidebarMenuItem as-child :is-active="route.path === ConsolePaths.products">
        <template #icon><LayoutGrid :size="15" /></template>
        <RouterLink :to="ConsolePaths.products" @click="emit('navigate')"
          ><span class="min-w-0 flex-1 truncate text-start">All products</span></RouterLink
        >
      </SidebarMenuItem>
      <li v-if="pinned.isFirstLoad.value" aria-hidden="true" class="flex flex-col gap-1.5 py-1">
        <SkeletonState v-for="index in 2" :key="index" class="h-8 w-full rounded-md" />
      </li>
      <li v-else-if="isEditing" class="py-1">
        <PinEditor :entries="pinned.entries.value" compact />
      </li>
      <template v-else>
        <ProductNavNode
          v-for="node in nodes"
          :key="node.product.slug"
          :product="node.product"
          :condition="conditions.get(node.product.slug)"
          :is-recent="node.isRecent"
          :is-expanded="isExpanded(node.product)"
          @toggle="toggle(node.product)"
          @navigate="emit('navigate')"
        />
        <li v-if="!pinnedProducts.length" class="px-2.5 py-1.5 text-xs text-muted-foreground">
          Pin a product from its menu to keep it here.
        </li>
      </template>
      <li v-if="pinned.entries.value.length > 1 || (isEditing && pinned.entries.value.length)" class="pt-0.5">
        <button
          type="button"
          class="inline-flex h-7 items-center gap-1.5 rounded-md px-2.5 text-xs font-medium text-muted-foreground hover:bg-muted hover:text-foreground focus-visible:outline-2 focus-visible:outline-ring"
          :aria-pressed="isEditing"
          @click="editing = !editing"
        >
          <Check v-if="isEditing" :size="12" /><Pencil v-else :size="12" />{{ isEditing ? "Done" : "Reorder pins" }}
        </button>
      </li>
    </ul>
  </li>
</template>
