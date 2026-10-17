<script lang="ts">
import type { PinnedEntry } from "@/application/pins";

/** A pin's mark. */
export interface PinIconProps {
  readonly entry: PinnedEntry;
  readonly size?: "xs" | "sm";
}
</script>
<script setup lang="ts">
import { CircleSlash } from "lucide-vue-next";
import { ProductIcon } from "@/presentation/products";

/** The product's or service's own icon, a workspace's tinted monogram, or a mark for a pin whose target is gone. */
defineOptions({ name: "PinIcon" });
const props = withDefaults(defineProps<PinIconProps>(), { size: "xs" });
defineSlots<{}>();
</script>
<template>
  <CircleSlash v-if="!props.entry.location" :size="props.size === 'xs' ? 16 : 20" class="text-muted-foreground" />
  <ProductIcon
    v-else
    :product-slug="props.entry.productSlug"
    :service="props.entry.service"
    :name="props.entry.label"
    :size="props.size"
  />
</template>
