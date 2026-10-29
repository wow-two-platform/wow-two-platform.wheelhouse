<script lang="ts">
import type { ProductFacts } from "@/domain/portfolio";
import type { Product } from "@/domain/products";

/** One product as a card on the Products page. */
export interface ProductTileProps {
  /** The product; null draws the card's placeholder during a first load. */
  readonly product: Product | null;
  /** Its pills, condition and trend; undefined while the targets load. */
  readonly facts: ProductFacts | undefined;
  /** The trend's weeks; empty while the summary loads. */
  readonly weeks: readonly string[];
  readonly isPinned?: boolean;
}
</script>
<script setup lang="ts">
import { computed } from "vue";
import { RouterLink } from "vue-router";
import { Pin, TriangleAlert } from "lucide-vue-next";
import { Badge, Card, CardLink, Text } from "@wow-two-beta/fe/presentation/display";
import { ConsolePaths } from "@/application/navigation";
import { ProductStatusRules, identityNoticeOf } from "@/domain/products";
import { EnvironmentOrder } from "@/domain/targets";
import EnvironmentPill from "./EnvironmentPill.vue";
import OpenSiteMenu from "./OpenSiteMenu.vue";
import ProductIcon from "./ProductIcon.vue";
import ProductMoreMenu from "./ProductMoreMenu.vue";
import ProductTrend from "./ProductTrend.vue";

/**
 * A product at a glance: its mark, name (with a pin when pinned and a warning when its manifest is ignored), a
 * two-line description, then one pill per environment and its deploy trend. The whole card opens the product — its
 * name is the SDK card link stretched over the card — while the open-site and overflow controls, and each pill's
 * details, sit above that link. An inactive product is dimmed and labelled.
 */
defineOptions({ name: "ProductTile" });
const props = withDefaults(defineProps<ProductTileProps>(), { isPinned: false });
const emit = defineEmits<{ edit: [] }>();
defineSlots<{}>();
const inactive = computed(() => Boolean(props.product && !ProductStatusRules.isActive(props.product)));
const notice = computed(() => identityNoticeOf(props.product));
</script>
<template>
  <Card
    role="article"
    variant="surface"
    tone="neutral"
    radius="2xl"
    padding="lg"
    :elevation="0"
    class="group flex min-w-0 flex-col gap-2.5 bg-card text-foreground transition-[border-color,box-shadow,opacity] duration-150 hover:border-primary/40 hover:shadow-sm motion-reduce:transition-none"
    :class="inactive ? 'opacity-60 hover:opacity-90' : ''"
  >
    <div class="flex min-w-0 items-center gap-3">
      <ProductIcon :product-slug="props.product?.slug ?? null" :name="props.product?.name ?? ''" />
      <div class="flex min-w-0 flex-1 items-center gap-1.5">
        <Text as="span" class="min-w-0 truncate text-sm font-semibold text-inherit" skeleton-width="8rem"
          ><CardLink v-if="props.product" as-child
            ><RouterLink :to="ConsolePaths.product(props.product.slug)" class="after:rounded-2xl">{{
              props.product.name
            }}</RouterLink></CardLink
          ></Text
        >
        <Pin v-if="props.isPinned" :size="13" class="shrink-0 fill-current text-primary" aria-label="Pinned" />
        <span v-if="notice" class="relative z-10 inline-flex shrink-0 text-warning" :title="notice"
          ><TriangleAlert :size="14" aria-hidden="true" /><span class="sr-only">{{ notice }}</span></span
        >
        <Badge v-if="inactive" variant="neutral" size="sm" class="shrink-0">Inactive</Badge>
      </div>
      <div v-if="props.product" class="relative z-10 -me-1.5 flex shrink-0 items-center gap-0.5">
        <OpenSiteMenu :product="props.product" />
        <ProductMoreMenu :product="props.product" @edit="emit('edit')" />
      </div>
    </div>
    <Text class="line-clamp-2 min-h-[2lh] text-[13px] leading-snug text-muted-foreground" skeleton-width="14rem">{{
      props.product ? props.product.description || props.product.slug : ""
    }}</Text>
    <div class="mt-auto flex min-w-0 items-center gap-2">
      <ul role="list" aria-label="Environments" class="flex min-w-0 flex-wrap items-center gap-1.5">
        <li v-for="(environment, index) in EnvironmentOrder" :key="environment" class="min-w-0">
          <EnvironmentPill :pill="props.product ? (props.facts?.pills[index] ?? null) : null" :environment="environment" />
        </li>
      </ul>
      <span class="ms-auto flex shrink-0 items-center"><ProductTrend :facts="props.product ? props.facts : undefined" :weeks="props.weeks" /></span>
    </div>
  </Card>
</template>
