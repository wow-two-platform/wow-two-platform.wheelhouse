<script lang="ts">
import type { Product } from "@/domain/products";

/** A product's overflow menu on its card or row. */
export interface ProductMoreMenuProps {
  readonly product: Product;
}
</script>
<script setup lang="ts">
import { computed } from "vue";
import { Ellipsis, Eye, EyeOff, Github, Pencil, Pin, PinOff } from "lucide-vue-next";
import { Button } from "@wow-two-beta/fe/presentation/actions";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuTrigger,
  MenuItem,
  MenuSeparator,
} from "@wow-two-beta/fe/presentation/nav";
import { usePins } from "@/application/pins";
import { useProducts } from "@/application/products";
import { PinKind } from "@/domain/pins";
import { ProductStatus, ProductStatusRules } from "@/domain/products";

/**
 * The product's less frequent actions: its repository on GitHub, editing it (the page owns the form), pinning it to
 * the sidebar, and marking it inactive or active again. Promotion lives on the Deployments page only. Pin and status
 * changes show at once and roll back if the host refuses them.
 */
defineOptions({ name: "ProductMoreMenu" });
const props = defineProps<ProductMoreMenuProps>();
const emit = defineEmits<{ edit: [] }>();
defineSlots<{}>();
const pins = usePins();
const products = useProducts();
const subject = computed(() => ({ kind: PinKind.Product, product: props.product }));
const pinned = computed(() => pins.isPinned(subject.value));
const active = computed(() => ProductStatusRules.isActive(props.product));
const repository = computed(() => props.product.repository.url || `https://github.com/${props.product.repository.name}`);

/** Opens the repository in a new tab. @internal */
function openRepository(): void {
  globalThis.open?.(repository.value, "_blank", "noopener,noreferrer");
}
</script>
<template>
  <DropdownMenu placement="bottom-end">
    <DropdownMenuTrigger as-child>
      <Button
        size="sm"
        shape="square"
        variant="ghost"
        tone="neutral"
        class="max-md:size-11"
        :aria-label="`More for ${props.product.name}`"
        :title="`More for ${props.product.name}`"
        ><Ellipsis :size="16"
      /></Button>
    </DropdownMenuTrigger>
    <DropdownMenuContent :aria-label="`More for ${props.product.name}`" class="w-56">
      <MenuItem @select="openRepository"><Github :size="15" />Repository</MenuItem>
      <MenuItem @select="emit('edit')"><Pencil :size="15" />Edit product</MenuItem>
      <MenuItem @select="pins.toggle(subject)"
        ><PinOff v-if="pinned" :size="15" /><Pin v-else :size="15" />{{ pinned ? "Unpin" : "Pin to the sidebar" }}</MenuItem
      >
      <MenuSeparator />
      <MenuItem v-if="active" @select="products.setStatus(props.product, ProductStatus.Inactive)"
        ><EyeOff :size="15" />Mark inactive</MenuItem
      >
      <MenuItem v-else @select="products.setStatus(props.product, ProductStatus.Active)"
        ><Eye :size="15" />Mark active</MenuItem
      >
    </DropdownMenuContent>
  </DropdownMenu>
</template>
