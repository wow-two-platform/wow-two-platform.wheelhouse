<script lang="ts">
import type { PortfolioGroup } from '@/application/portfolio';
import type { useProductReadings } from '@/application/portfolio';

/** One workspace's products on the home page. */
export interface WorkspaceSectionProps {
  /** The workspace and the products to show. */
  readonly group: PortfolioGroup;
  /** The environment health and last deploy of each product. */
  readonly readings: Pick<ReturnType<typeof useProductReadings>, 'environmentsOf' | 'lastDeployOf' | 'historyLoading'>;
  /** Draws the cards as placeholders during the first load. */
  readonly isLoading?: boolean;
  /** The heading level, so a workspace page and the home page keep an outline. */
  readonly headingLevel?: 2 | 3;
}
</script>
<script setup lang="ts">
import { computed } from 'vue';
import { RouterLink } from 'vue-router';
import { useSkeletonItems } from '@wow-two-beta/fe/presentation/feedback';
import { ConsolePaths } from '@/application/navigation';
import ProductCard from './ProductCard.vue';

/** A workspace's name, product count and product cards; the actions slot holds the workspace's pin toggle. */
defineOptions({ name: 'WorkspaceSection' });
const props = withDefaults(defineProps<WorkspaceSectionProps>(), { isLoading: false, headingLevel: 2 });
defineSlots<{ actions?(): unknown }>();
const cards = useSkeletonItems(() => (props.isLoading ? undefined : props.group.products), 3);
const name = computed(() => props.group.workspace?.name ?? 'No workspace');
const hidden = computed(() => props.group.total - props.group.products.length);
const headingId = computed(() => `workspace-${props.group.workspace?.slug ?? 'none'}`);
</script>
<template>
  <section :aria-labelledby="headingId" class="min-w-0">
    <header class="mb-3 flex min-w-0 flex-wrap items-center justify-between gap-x-3 gap-y-1">
      <div class="flex min-w-0 items-baseline gap-2">
        <component :is="`h${props.headingLevel}`" :id="headingId" class="truncate text-base font-semibold">
          <RouterLink
            v-if="props.group.workspace"
            :to="ConsolePaths.workspace(props.group.workspace.slug)"
            class="hover:text-primary focus-visible:outline-2 focus-visible:outline-ring"
            >{{ name }}</RouterLink
          >
          <template v-else>{{ name }}</template>
        </component>
        <span v-if="!props.isLoading" class="shrink-0 text-xs tabular-nums text-muted-foreground"
          >{{ props.group.products.length }} product{{ props.group.products.length === 1 ? '' : 's'
          }}<template v-if="hidden > 0"> · {{ hidden }} inactive hidden</template></span
        >
      </div>
      <div class="flex shrink-0 items-center gap-1"><slot name="actions" /></div>
    </header>
    <p v-if="props.group.workspace?.description" class="-mt-2 mb-3 text-sm text-muted-foreground">
      {{ props.group.workspace.description }}
    </p>
    <div v-if="cards.length" class="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
      <ProductCard
        v-for="(product, index) in cards"
        :key="product?.slug ?? `placeholder-${index}`"
        :product="product ?? null"
        :environments="product ? props.readings.environmentsOf(product.slug) : undefined"
        :last-deploy="product ? props.readings.lastDeployOf(product.slug) : null"
        :history-loading="props.readings.historyLoading.value"
      />
    </div>
    <p v-else class="rounded-2xl border border-dashed border-border p-4 text-sm text-muted-foreground">
      {{ hidden > 0 ? 'Only inactive products; turn on Show inactive to see them.' : 'No products in this workspace yet.' }}
    </p>
  </section>
</template>
