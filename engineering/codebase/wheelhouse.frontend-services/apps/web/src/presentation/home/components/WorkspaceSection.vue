<script lang="ts">
import type { PortfolioGroup } from '@/application/portfolio';
import type { ProductFacts } from '@/domain/portfolio';
import type { Product } from '@/domain/products';

/** One workspace's products on the home page. */
export interface WorkspaceSectionProps {
  /** The workspace and the products to show. */
  readonly group: PortfolioGroup;
  /** Each product's pills, condition and trend; undefined while the targets load. */
  readonly factsOf: (slug: string) => ProductFacts | undefined;
  /** The trends' weeks; empty while the summary loads. */
  readonly weeks: readonly string[];
  readonly isPinned: (product: Product) => boolean;
  /** Draws the cards as placeholders during the first load. */
  readonly isLoading?: boolean;
}
</script>
<script setup lang="ts">
import { computed } from 'vue';
import { pluralize } from "@wow-two-beta/fe/foundation/formatters";
import { RouterLink } from 'vue-router';
import { useSkeletonItems } from '@wow-two-beta/fe/presentation/feedback';
import { ConsolePaths } from '@/application/navigation';
import ProductTile from '@/presentation/products/components/ProductTile.vue';

/**
 * A workspace's name, product count and its products as the Products page draws them: mark, name, description, one
 * pill per environment and the deploy trend. The name opens the workspace's group on the Products page.
 */
defineOptions({ name: 'WorkspaceSection' });
const props = withDefaults(defineProps<WorkspaceSectionProps>(), { isLoading: false });
const emit = defineEmits<{ edit: [product: Product] }>();
defineSlots<{}>();
const cards = useSkeletonItems(() => (props.isLoading ? undefined : props.group.products), 3);
const name = computed(() => props.group.workspace?.name ?? 'No workspace');
const hidden = computed(() => props.group.total - props.group.products.length);
const headingId = computed(() => `home-workspace-${props.group.workspace?.slug ?? 'none'}`);
</script>
<template>
  <section :aria-labelledby="headingId" class="min-w-0">
    <header class="mb-3 flex min-w-0 items-baseline gap-2">
      <h2 :id="headingId" class="truncate text-base font-semibold">
        <RouterLink
          v-if="props.group.workspace"
          :to="ConsolePaths.workspace(props.group.workspace.slug)"
          class="hover:text-primary focus-visible:outline-2 focus-visible:outline-ring"
          >{{ name }}</RouterLink
        >
        <template v-else>{{ name }}</template>
      </h2>
      <span v-if="!props.isLoading" class="shrink-0 text-xs tabular-nums text-muted-foreground"
        >{{ props.group.products.length }} {{ pluralize(props.group.products.length, 'product') }}<template v-if="hidden > 0"> · {{ hidden }} inactive hidden</template></span
      >
    </header>
    <ul v-if="cards.length" role="list" class="grid gap-3 md:grid-cols-2 xl:grid-cols-3" :aria-label="name">
      <li v-for="(product, index) in cards" :key="product?.slug ?? `placeholder-${index}`" class="flex min-w-0">
        <ProductTile
          class="flex-1"
          :product="product ?? null"
          :facts="product ? props.factsOf(product.slug) : undefined"
          :weeks="props.weeks"
          :is-pinned="product ? props.isPinned(product) : false"
          @edit="product && emit('edit', product)"
        />
      </li>
    </ul>
    <p v-else class="rounded-2xl border border-dashed border-border p-4 text-sm text-muted-foreground">
      {{ hidden > 0 ? 'Only inactive products; turn on Show inactive to see them.' : 'No products in this workspace yet.' }}
    </p>
  </section>
</template>
