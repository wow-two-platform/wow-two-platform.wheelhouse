<script lang="ts">
import type { ProductFacts } from "@/domain/portfolio";
import type { Product } from "@/domain/products";

/** One product as a row on the Products page, from tablets up. */
export interface ProductListRowProps {
  /** The product; null draws the row's placeholder during a first load. */
  readonly product: Product | null;
  readonly facts: ProductFacts | undefined;
  readonly weeks: readonly string[];
  readonly isPinned?: boolean;
}

/** The row's columns: who, dev, prod, trend, actions — shared with the list's header. */
export const ProductRowColumns = "grid-cols-[minmax(0,1fr)_8.5rem_8.5rem_4.75rem_4.5rem]";
</script>
<script setup lang="ts">
import { computed } from "vue";
import { RouterLink } from "vue-router";
import { Pin, TriangleAlert } from "lucide-vue-next";
import { Badge, CardLink, Text } from "@wow-two-beta/fe/presentation/display";
import { ConsolePaths } from "@/application/navigation";
import { ProductStatusRules, identityNoticeOf } from "@/domain/products";
import { EnvironmentOrder } from "@/domain/targets";
import EnvironmentPill from "./EnvironmentPill.vue";
import OpenSiteMenu from "./OpenSiteMenu.vue";
import ProductIcon from "./ProductIcon.vue";
import ProductMoreMenu from "./ProductMoreMenu.vue";
import ProductTrend from "./ProductTrend.vue";

/**
 * A product in one line: mark, name and description, a column per environment's pill, the deploy trend, then the
 * open-site and overflow controls. The whole row opens the product through its name's stretched link; the description
 * gives way below `lg`.
 */
defineOptions({ name: "ProductListRow" });
const props = withDefaults(defineProps<ProductListRowProps>(), { isPinned: false });
const emit = defineEmits<{ edit: [] }>();
defineSlots<{}>();
const inactive = computed(() => Boolean(props.product && !ProductStatusRules.isActive(props.product)));
const notice = computed(() => identityNoticeOf(props.product));
</script>
<template>
  <li
    class="relative grid h-[52px] items-center gap-3 border-t border-border ps-3.5 pe-2 transition-colors first:border-t-0 hover:bg-muted/40 has-[a[data-card-link]:focus-visible]:outline-2 has-[a[data-card-link]:focus-visible]:-outline-offset-2 has-[a[data-card-link]:focus-visible]:outline-ring"
    :class="[ProductRowColumns, inactive ? '*:opacity-60' : '']"
  >
    <div class="flex min-w-0 items-center gap-3">
      <ProductIcon :product-slug="props.product?.slug ?? null" :name="props.product?.name ?? ''" size="sm" />
      <Text as="span" class="shrink-0 truncate text-sm font-semibold text-inherit max-lg:min-w-0 max-lg:shrink" skeleton-width="7rem"
        ><CardLink v-if="props.product" as-child
          ><RouterLink :to="ConsolePaths.product(props.product.slug)">{{ props.product.name }}</RouterLink></CardLink
        ></Text
      >
      <Pin v-if="props.isPinned" :size="12" class="-ms-1.5 shrink-0 fill-current text-primary" aria-label="Pinned" />
      <span v-if="notice" class="relative z-10 -ms-1.5 inline-flex shrink-0 text-warning" :title="notice"
        ><TriangleAlert :size="13" aria-hidden="true" /><span class="sr-only">{{ notice }}</span></span
      >
      <Badge v-if="inactive" variant="neutral" size="sm" class="shrink-0">Inactive</Badge>
      <span v-if="props.product" class="min-w-0 truncate text-[13px] text-muted-foreground max-lg:hidden">{{
        props.product.description
      }}</span>
    </div>
    <div
      v-for="(environment, index) in EnvironmentOrder"
      :key="environment"
      class="min-w-0"
    >
      <EnvironmentPill :pill="props.product ? (props.facts?.pills[index] ?? null) : null" :environment="environment" />
    </div>
    <div class="flex justify-end"><ProductTrend :facts="props.product ? props.facts : undefined" :weeks="props.weeks" /></div>
    <div v-if="props.product" class="relative z-10 flex items-center justify-end gap-0.5">
      <OpenSiteMenu :product="props.product" />
      <ProductMoreMenu :product="props.product" @edit="emit('edit')" />
    </div>
    <div v-else />
  </li>
</template>
