<script lang="ts">
import type { ProductCondition } from "@/domain/portfolio";

/** A product's condition as a small mark beside its name in the navigation. */
export interface ProductStatusMarkProps {
  readonly condition: ProductCondition | undefined;
  /** Sits on the corner of an icon in the rail rather than after a name. */
  readonly isBadge?: boolean;
}
</script>
<script setup lang="ts">
import { computed } from "vue";
import { ProductConditionWords } from "@/domain/portfolio";

/**
 * Marks only what needs the owner — failing in the danger tone, needing a look in the warning tone, a live rollout in
 * the info tone — and nothing for a calm or idle product, so the navigation stays quiet. The word rides along for
 * assistive technology and as the title.
 */
defineOptions({ name: "ProductStatusMark" });
const props = withDefaults(defineProps<ProductStatusMarkProps>(), { isBadge: false });
defineSlots<{}>();
const tone = computed(() =>
  props.condition === "failing"
    ? "bg-destructive"
    : props.condition === "attention"
      ? "bg-warning"
      : props.condition === "deploying"
        ? "bg-info animate-pulse motion-reduce:animate-none"
        : null,
);
</script>
<template>
  <span
    v-if="tone && props.condition"
    :title="ProductConditionWords[props.condition]"
    class="inline-flex shrink-0 rounded-full"
    :class="[tone, props.isBadge ? 'absolute -end-1 -top-1 size-2.5 ring-2 ring-sidebar' : 'size-2']"
    ><span class="sr-only">{{ ProductConditionWords[props.condition] }}</span></span
  >
</template>
