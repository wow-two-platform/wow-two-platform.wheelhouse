<script lang="ts">
import type { DeploymentJob } from '@/domain/deployments';
import type { EnvironmentReading } from '@/domain/overview';
import type { Product } from '@/domain/products';

/** One product on the home page or a workspace page. */
export interface ProductCardProps {
  /** The product; null draws the card's placeholder during a first load. */
  readonly product: Product | null;
  readonly environments?: readonly EnvironmentReading[] | undefined;
  /** The newest deployment to any of its environments; null when it has none. */
  readonly lastDeploy?: DeploymentJob | null | undefined;
  /** True while the deployment history is still loading, so the last deploy draws a placeholder. */
  readonly historyLoading?: boolean;
}
</script>
<script setup lang="ts">
import { computed } from 'vue';
import { RouterLink } from 'vue-router';
import { Badge, RelativeTime, Text } from '@wow-two-beta/fe/presentation/display';
import { ConsolePaths } from '@/application/navigation';
import { DeploymentExtensions } from '@/domain/deployments';
import { ProductStatusRules } from '@/domain/products';
import { ProductIcon } from '@/presentation/products';
import EnvironmentDots from './EnvironmentDots.vue';

/**
 * A product at a glance: its mark, name, dev/test/prod health and last deploy. The whole card opens the product; an
 * inactive product is dimmed and labelled.
 */
defineOptions({ name: 'ProductCard' });
const props = withDefaults(defineProps<ProductCardProps>(), {
  environments: undefined,
  lastDeploy: null,
  historyLoading: false,
});
defineSlots<{}>();
const inactive = computed(() => Boolean(props.product && !ProductStatusRules.isActive(props.product)));
</script>
<template>
  <component
    :is="props.product ? RouterLink : 'div'"
    v-bind="props.product ? { to: ConsolePaths.product(props.product.slug) } : {}"
    class="group flex min-w-0 flex-col gap-3 rounded-2xl border border-border bg-card p-4 transition-[border-color,box-shadow,opacity] duration-150 hover:border-primary/40 hover:shadow-sm focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring motion-reduce:transition-none"
    :class="inactive ? 'opacity-60 hover:opacity-90' : ''"
  >
    <div class="flex min-w-0 items-center gap-3">
      <ProductIcon :product-slug="props.product?.slug ?? null" :name="props.product?.name ?? ''" />
      <div class="min-w-0 flex-1">
        <Text class="truncate text-sm font-semibold text-inherit" skeleton-width="7rem">{{ props.product?.name }}</Text>
        <Text class="mt-0.5 truncate text-xs text-muted-foreground" skeleton-width="10rem">{{
          props.product ? props.product.description || props.product.slug : ''
        }}</Text>
      </div>
      <Badge v-if="inactive" variant="neutral" size="sm">Inactive</Badge>
    </div>
    <EnvironmentDots :environments="props.product ? props.environments : undefined" />
    <div class="flex min-w-0 items-center justify-between gap-3 border-t border-border pt-3 text-xs text-muted-foreground">
      <span class="shrink-0">Last deploy</span>
      <Text
        v-if="!props.product || props.historyLoading"
        as="span"
        is-skeleton
        skeleton-width="6rem"
        class="text-xs"
      />
      <span v-else-if="props.lastDeploy" class="flex min-w-0 items-center gap-1.5">
        <span
          class="size-1.5 shrink-0 rounded-full"
          :class="{
            'bg-success': DeploymentExtensions.statusVariant(props.lastDeploy.status) === 'success',
            'bg-destructive': DeploymentExtensions.statusVariant(props.lastDeploy.status) === 'danger',
            'bg-warning': DeploymentExtensions.statusVariant(props.lastDeploy.status) === 'warning',
            'bg-info': DeploymentExtensions.statusVariant(props.lastDeploy.status) === 'info',
          }"
          :title="DeploymentExtensions.label(props.lastDeploy.status)"
        />
        <span class="truncate font-medium text-foreground">{{ props.lastDeploy.release ?? 'Build' }}</span>
        <RelativeTime v-if="props.lastDeploy.submittedAt" :value="props.lastDeploy.submittedAt" class="shrink-0" />
      </span>
      <span v-else>None yet</span>
    </div>
  </component>
</template>
