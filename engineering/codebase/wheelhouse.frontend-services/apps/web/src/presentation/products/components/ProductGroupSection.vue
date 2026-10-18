<script lang="ts">
import type { ProductsView } from "@/application/preferences";
import type { ProductFacts } from "@/domain/portfolio";
import type { Product } from "@/domain/products";
import type { Workspace } from "@/domain/workspaces";

/** One workspace's products on the Products page. */
export interface ProductGroupSectionProps {
  /** The workspace; null for products without one, or for a placeholder group during the first load. */
  readonly workspace: Workspace | null;
  /** The products the filter keeps, in the sort's order; null entries draw placeholders. */
  readonly products: readonly (Product | null)[];
  /** The group's element id, which the workspace cards scroll to. */
  readonly groupId: string;
  /** Cards or rows; the page passes cards on phones whatever the choice. */
  readonly view: ProductsView;
  readonly factsOf: (slug: string) => ProductFacts | undefined;
  readonly weeks: readonly string[];
  readonly isPinned: (product: Product) => boolean;
  readonly isExpanded: boolean;
  /** What the group says when the filter leaves it empty. */
  readonly emptyText: string;
  readonly isLoading?: boolean;
}
</script>
<script setup lang="ts">
import { ChevronDown } from "lucide-vue-next";
import { Text } from "@wow-two-beta/fe/presentation/display";
import ProductListRow from "./ProductListRow.vue";
import ProductTile from "./ProductTile.vue";

/**
 * A workspace's products under a minimal separator — its name, how many it shows and a fold control, remembered per
 * workspace — as cards or rows. The workspace's figures live on its card above, not here.
 */
defineOptions({ name: "ProductGroupSection" });
const props = withDefaults(defineProps<ProductGroupSectionProps>(), { isLoading: false });
const emit = defineEmits<{ toggle: []; edit: [product: Product] }>();
defineSlots<{}>();
</script>
<template>
  <section :id="props.groupId" :aria-labelledby="`${props.groupId}-title`" class="min-w-0 scroll-mt-4">
    <header class="mb-3 flex min-w-0 items-center gap-2">
      <button
        type="button"
        class="-ms-1.5 inline-flex min-w-0 items-center gap-2 rounded-md px-1.5 py-1 text-start hover:bg-muted/60 focus-visible:outline-2 focus-visible:outline-ring max-md:min-h-11"
        :aria-expanded="props.isExpanded"
        :aria-controls="`${props.groupId}-list`"
        :disabled="props.isLoading"
        @click="emit('toggle')"
      >
        <ChevronDown
          :size="16"
          class="shrink-0 text-muted-foreground transition-transform duration-150 motion-reduce:transition-none"
          :class="props.isExpanded ? '' : '-rotate-90'"
          aria-hidden="true"
        />
        <Text as="h3" :id="`${props.groupId}-title`" class="truncate text-base font-semibold text-inherit" skeleton-width="8rem">{{
          props.isLoading ? "" : (props.workspace?.name ?? "No workspace")
        }}</Text>
        <span v-if="!props.isLoading" class="shrink-0 text-xs tabular-nums text-muted-foreground"
          >{{ props.products.length }} product{{ props.products.length === 1 ? "" : "s" }}</span
        >
      </button>
      <span class="h-px min-w-6 flex-1 bg-border" aria-hidden="true" />
    </header>
    <div v-show="props.isExpanded" :id="`${props.groupId}-list`">
      <p
        v-if="!props.products.length"
        class="rounded-2xl border border-dashed border-border p-4 text-sm text-muted-foreground"
      >
        {{ props.emptyText }}
      </p>
      <ul
        v-else-if="props.view === 'rows'"
        role="list"
        class="overflow-hidden rounded-2xl border border-border bg-card"
        :aria-label="props.workspace?.name ?? 'No workspace'"
      >
        <ProductListRow
          v-for="(product, index) in props.products"
          :key="product?.slug ?? `placeholder-${index}`"
          :product="product"
          :facts="product ? props.factsOf(product.slug) : undefined"
          :weeks="props.weeks"
          :is-pinned="product ? props.isPinned(product) : false"
          @edit="product && emit('edit', product)"
        />
      </ul>
      <ul
        v-else
        role="list"
        class="grid gap-3 md:grid-cols-2 xl:grid-cols-3"
        :aria-label="props.workspace?.name ?? 'No workspace'"
      >
        <li v-for="(product, index) in props.products" :key="product?.slug ?? `placeholder-${index}`" class="flex min-w-0">
          <ProductTile
            class="flex-1"
            :product="product"
            :facts="product ? props.factsOf(product.slug) : undefined"
            :weeks="props.weeks"
            :is-pinned="product ? props.isPinned(product) : false"
            @edit="product && emit('edit', product)"
          />
        </li>
      </ul>
    </div>
  </section>
</template>
