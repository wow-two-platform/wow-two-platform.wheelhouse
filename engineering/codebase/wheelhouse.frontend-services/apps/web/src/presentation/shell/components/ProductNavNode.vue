<script lang="ts">
import type { ProductCondition } from "@/domain/portfolio";
import type { Product } from "@/domain/products";

/** One product in the sidebar's Products tree: its row and, unfolded, its tabs. */
export interface ProductNavNodeProps {
  readonly product: Product;
  readonly condition: ProductCondition | undefined;
  readonly isExpanded: boolean;
  /** Marks the product last opened that is not pinned, so it reads as a visit rather than a pin. */
  readonly isRecent?: boolean;
}
</script>
<script setup lang="ts">
import { computed } from "vue";
import { RouterLink, useRoute } from "vue-router";
import { ChevronRight, History } from "lucide-vue-next";
import { SidebarMenuItem } from "@wow-two-beta/fe/presentation/nav";
import { ConsolePaths, ProductTab, ProductTabs, productTabOf } from "@/application/navigation";
import { ProductStatusRules } from "@/domain/products";
import { ProductIcon } from "@/presentation/products";
import ProductStatusMark from "./ProductStatusMark.vue";

/**
 * A product row whose name opens its page and whose chevron unfolds its tabs — Overview, Releases, Secrets, Activity,
 * Deployments — each one click away. It shows the product's mark, its name, and a status mark only when something
 * needs the owner. The current product's row and tab are selected.
 */
defineOptions({ name: "ProductNavNode" });
const props = withDefaults(defineProps<ProductNavNodeProps>(), { isRecent: false });
const emit = defineEmits<{ toggle: []; navigate: [] }>();
defineSlots<{}>();
const route = useRoute();
const isCurrent = computed(() => route.params["product"] === props.product.slug);
const currentTab = computed(() => {
  const value = route.params["tab"];
  return (productTabOf(typeof value === "string" ? value : null) ?? ProductTabs[0]!).value;
});
const listId = computed(() => `product-nav-${props.product.slug}`);
</script>
<template>
  <li class="flex flex-col">
    <div class="flex items-center gap-0.5">
      <ul role="list" class="min-w-0 flex-1">
        <SidebarMenuItem as-child :is-active="isCurrent && currentTab === ProductTab.Overview">
          <template #icon><ProductIcon :product-slug="props.product.slug" :name="props.product.name" size="xs" /></template>
          <template #trailing
            ><span class="inline-flex items-center gap-1.5"
              ><History
                v-if="props.isRecent"
                :size="12"
                class="text-muted-foreground"
                aria-label="Last opened"
              /><ProductStatusMark :condition="props.condition" /></span
          ></template>
          <RouterLink
            :to="ConsolePaths.product(props.product.slug)"
            :class="ProductStatusRules.isActive(props.product) ? '' : 'opacity-60'"
            :aria-current="isCurrent ? 'page' : undefined"
            :title="props.isRecent ? `${props.product.name} — last opened` : props.product.name"
            @click="emit('navigate')"
            ><span class="min-w-0 flex-1 truncate text-start">{{ props.product.name }}</span></RouterLink
          >
        </SidebarMenuItem>
      </ul>
      <button
        type="button"
        class="inline-flex size-7 shrink-0 items-center justify-center rounded-md text-muted-foreground transition-colors hover:bg-muted hover:text-foreground focus-visible:outline-2 focus-visible:outline-ring"
        :aria-expanded="props.isExpanded"
        :aria-controls="listId"
        :aria-label="`${props.isExpanded ? 'Hide' : 'Show'} the tabs of ${props.product.name}`"
        @click="emit('toggle')"
      >
        <ChevronRight
          :size="14"
          class="transition-transform duration-150 motion-reduce:transition-none"
          :class="props.isExpanded ? 'rotate-90' : ''"
        />
      </button>
    </div>
    <ul
      v-if="props.isExpanded"
      :id="listId"
      role="list"
      :aria-label="`${props.product.name} tabs`"
      class="ms-[1.125rem] flex flex-col gap-0.5 border-s border-border ps-2 pt-0.5"
    >
      <SidebarMenuItem
        v-for="tab in ProductTabs.filter((entry) => entry.value !== ProductTab.Overview)"
        :key="tab.value"
        as-child
        :is-active="isCurrent && currentTab === tab.value"
      >
        <RouterLink :to="ConsolePaths.product(props.product.slug, tab.value)" class="h-8 text-[13px]" @click="emit('navigate')"
          ><span class="min-w-0 flex-1 truncate text-start">{{ tab.label }}</span></RouterLink
        >
      </SidebarMenuItem>
    </ul>
  </li>
</template>
