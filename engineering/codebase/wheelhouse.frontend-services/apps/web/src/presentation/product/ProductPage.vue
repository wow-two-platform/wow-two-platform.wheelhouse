<script setup lang="ts">
import { computed, defineAsyncComponent, nextTick, useTemplateRef, watch } from "vue";
import { RouterLink, useRoute, useRouter } from "vue-router";
import { ChevronLeft } from "lucide-vue-next";
import { Button } from "@wow-two-beta/fe/presentation/actions";
import {
  EmptyState,
  TabsGroup,
  TabsGroupList,
  TabsGroupPanel,
  TabsGroupTab,
} from "@wow-two-beta/fe/presentation/display";
import { DataRegion } from "@wow-two-beta/fe/presentation/feedback";
import { ConsolePaths, ProductTab, ProductTabs, productTabOf } from "@/application/navigation";
import { workspaceOf } from "@/application/portfolio";
import { buildProductInventory } from "@/application/product/ProductInventory";
import { useLastProduct } from "@/application/preferences";
import { useProducts } from "@/application/products";
import { useWorkspaces } from "@/application/workspaces";
import { useDeploymentTargets } from "@/application/deployments";
import ProductHeader from "./ProductHeader.vue";
import ProductOverview from "./tabs/ProductOverview.vue";

/* Overview ships with the page; the other tabs load the first time they open. @internal */
const ProductSecrets = defineAsyncComponent(() => import("./tabs/ProductSecrets.vue"));
const ProductActivity = defineAsyncComponent(() => import("./tabs/ProductActivity.vue"));
const ProductDeployments = defineAsyncComponent(() => import("./tabs/ProductDeployments.vue"));

/**
 * One product on its own page: the header — what it is and what applies to all of it — over its tabs: Overview
 * (environments, services, comparison, inspector), Deployments (the history and the releases it can deploy), Secrets
 * and Activity. Each tab has its own address,
 * `/p/{product}/{tab}`, with Overview at `/p/{product}`; the tabs scroll sideways on phones. A tab's top-bar actions
 * come from its panel. A new tab adds its entry to `ProductTabs` (application/navigation) and its panel below. The page
 * it shows is remembered — product and tab — so the sidebar's Products returns to it; phones get "All products" at the
 * top, since their bottom bar's Products returns here too.
 */
defineOptions({ name: "ProductPage" });
const route = useRoute();
const router = useRouter();
const products = useProducts();
const workspaces = useWorkspaces();
const targets = useDeploymentTargets();
const header = useTemplateRef<{ addEnvironment(): void }>("header");
const lastProduct = useLastProduct();

const productKey = computed(() => param(route.params["product"]) ?? "");
const tabSegment = computed(() => param(route.params["tab"]));
/** The tab the address names; an unknown one falls back to Overview, and the address is corrected below. */
const tab = computed(() => productTabOf(tabSegment.value) ?? ProductTabs[0]!);
const inventory = computed(() => buildProductInventory(products.products.value, targets.data.value ?? []));
const product = computed(() => inventory.value.products.find((item) => item.key === productKey.value) ?? null);
const record = computed(() => product.value?.product ?? null);
const workspace = computed(() => workspaceOf(workspaces.workspaces.value, record.value));
/** The first read: the header draws its frame with placeholder values until the inventory arrives. */
const firstLoad = computed(() => products.loading.value || targets.loading.value);
const readFailed = computed(
  () => (Boolean(products.error.value) && !products.products.value.length) || (Boolean(targets.error.value) && !targets.data.value),
);
const missing = computed(() => !firstLoad.value && !readFailed.value && !product.value);

/** Reads a path parameter as one string, or null without one. @internal */
function param(value: unknown): string | null {
  const item = Array.isArray(value) ? value[0] : value;
  return typeof item === "string" && item ? item : null;
}

/** Opens a tab at its own address. Overview keeps the environment and inspector; the others keep the environment. */
function chooseTab(value: string): void {
  if (value === tab.value.value) return;
  const env = typeof route.query["env"] === "string" ? route.query["env"] : null;
  const query = value === ProductTabs[0]!.value ? { ...route.query } : env ? { env } : {};
  void router.push({ path: ConsolePaths.product(productKey.value, value), query });
}

/* On a phone the tab list scrolls sideways; the chosen tab, such as Activity at its end, scrolls into view. */
watch(
  () => [tab.value.value, firstLoad.value] as const,
  async () => {
    await nextTick();
    document
      .querySelector<HTMLElement>('[aria-label="Product sections"] [role="tab"][aria-selected="true"]')
      ?.scrollIntoView({ block: "nearest", inline: "nearest" });
  },
  { immediate: true, flush: "post" },
);

/* The product shown, and its tab, become the place the Products destination returns to. */
watch(
  () => [record.value?.slug ?? null, tab.value.value] as const,
  ([slug, value]) => {
    if (slug) lastProduct.value = { slug, tab: value === ProductTab.Overview ? null : value };
  },
  { immediate: true },
);

/* An address naming no tab opens Overview, keeping its query. */
watch(
  tabSegment,
  (segment) => {
    if (segment && !productTabOf(segment))
      void router.replace({ path: ConsolePaths.product(productKey.value), query: route.query });
  },
  { immediate: true },
);
</script>
<template>
  <DataRegion
    :is-first-load="firstLoad"
    :error="readFailed ? (products.error.value ?? targets.error.value) : null"
    :has-data="!readFailed"
    :is-empty="missing"
    empty-title="This product is not in Wheelhouse"
    empty-description="It may have been removed or renamed. Find it in Products."
    loading-label="Loading the product"
    class="flex flex-col gap-5"
  >
    <template #emptyActions
      ><Button as-child variant="outline"><RouterLink :to="ConsolePaths.home">Home</RouterLink></Button
      ><Button as-child><RouterLink :to="ConsolePaths.products">Products</RouterLink></Button></template
    >
    <!-- Phones have no sidebar: the way back to the grid sits above the header. -->
    <RouterLink
      :to="ConsolePaths.products"
      class="-my-2 inline-flex min-h-11 items-center gap-1 self-start rounded-md pe-2 text-sm font-medium text-muted-foreground hover:text-foreground focus-visible:outline-2 focus-visible:outline-ring md:hidden"
      ><ChevronLeft :size="16" aria-hidden="true" />All products</RouterLink
    >
    <ProductHeader ref="header" :product="firstLoad ? null : product" :workspace="workspace" />
    <TabsGroup :model-value="tab.value" activation-mode="manual" class="min-w-0 gap-5" @update:model-value="chooseTab">
      <!-- The list scrolls sideways inside itself when the tabs outgrow a phone; the page never does. -->
      <TabsGroupList
        aria-label="Product sections"
        class="flex w-full max-w-full overflow-x-auto overflow-y-hidden [scrollbar-width:none]"
      >
        <TabsGroupTab
          v-for="entry in ProductTabs"
          :key="entry.value"
          :value="entry.value"
          class="shrink-0 whitespace-nowrap max-md:min-h-11"
          >{{ entry.label }}</TabsGroupTab
        >
      </TabsGroupList>
      <!-- Only the chosen tab's panel renders, so a tab's reads and top-bar actions start when it opens. -->
      <TabsGroupPanel :value="ProductTab.Overview" class="min-w-0">
        <ProductOverview @add-environment="header?.addEnvironment()" />
      </TabsGroupPanel>
      <TabsGroupPanel :value="ProductTab.Deployments" class="min-w-0">
        <ProductDeployments :product="productKey" />
      </TabsGroupPanel>
      <TabsGroupPanel :value="ProductTab.Secrets" class="min-w-0">
        <ProductSecrets v-if="record" :product="record" />
        <EmptyState
          v-else-if="!firstLoad"
          title="Not in the product catalog"
          description="Secrets belong to a product Wheelhouse holds; add this one in Products to manage its namespaces."
        />
      </TabsGroupPanel>
      <TabsGroupPanel :value="ProductTab.Activity" class="min-w-0">
        <ProductActivity :product="productKey" />
      </TabsGroupPanel>
    </TabsGroup>
  </DataRegion>
</template>
