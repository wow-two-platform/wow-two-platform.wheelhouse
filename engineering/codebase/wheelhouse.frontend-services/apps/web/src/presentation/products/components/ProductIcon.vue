<script lang="ts">
import type { IconColor, IconForm } from "@/domain/products";

/** A product's mark: the icon its repository carries for the active theme, else a monogram tinted by its name. */
export interface ProductIconProps {
  /** The catalog slug; without one (a product outside the catalog) the monogram shows. */
  readonly productSlug?: string | null | undefined;
  /** A service of the product, whose own icon is preferred; the product's icon answers when it has none. */
  readonly service?: string | null | undefined;
  /** The product's name, for the monogram and its tint. */
  readonly name: string;
  /** `xs` sits in a navigation row; `sm`, `md` and `lg` in lists, cards and headers. */
  readonly size?: "xs" | "sm" | "md" | "lg";
  /** The symbol alone beside a name, the symbol with its wordmark where the icon stands alone. */
  readonly form?: IconForm | undefined;
  /** The brand's colors, or one flat ink on a surface that tints its icons. */
  readonly color?: IconColor | undefined;
}

/** @internal The rounding and monogram type per size; the image keeps a small inset so edge-to-edge marks still breathe. */
const SizeClass = {
  xs: "size-5 rounded-md text-[10px] [&>img]:p-0.5!",
  sm: "size-8 rounded-lg text-xs",
  md: "size-10 rounded-lg text-sm",
  lg: "size-12 rounded-xl text-base",
} as const;
</script>
<script setup lang="ts">
import { computed } from "vue";
import { useProductIcon } from "@/application/products/useProductIcon";
import { useColorMode } from "@wow-two-beta/fe/foundation/primitives";
import { Avatar } from "@wow-two-beta/fe/presentation/display";

/**
 * Renders the product's own icon for the active theme on the SDK `Avatar`, falling back to a stable tinted monogram so
 * products stay distinguishable. A refused image is recorded through the captured `error` event, so the monogram takes
 * its tint and stays until the address changes.
 */
defineOptions({ name: "ProductIcon" });
const props = withDefaults(defineProps<ProductIconProps>(), { size: "md" });
defineSlots<{}>();

const colorMode = useColorMode();
const icon = useProductIcon({
  slug: () => props.productSlug,
  service: () => props.service,
  name: () => props.name,
  theme: () => colorMode.mode,
  form: () => props.form,
  color: () => props.color,
});
/** Mixing with theme tokens keeps the tint readable in light and dark. */
const tint = computed(() => ({
  backgroundColor: `color-mix(in oklch, hsl(${icon.hue.value} 70% 50%) 18%, var(--color-card))`,
  color: `color-mix(in oklch, hsl(${icon.hue.value} 70% 40%) 75%, var(--color-foreground))`,
}));
</script>
<template>
  <Avatar
    shape="square"
    :size="props.size === 'xs' ? 'sm' : props.size"
    v-bind="icon.src.value ? { src: icon.src.value } : {}"
    :fallback="icon.monogram.value"
    alt=""
    class="relative flex border border-border font-semibold [&>img]:object-contain [&>img]:p-1"
    :class="[SizeClass[props.size], icon.src.value ? 'bg-card' : '']"
    :style="icon.src.value ? undefined : tint"
    @error.capture="icon.failed()"
  />
</template>
