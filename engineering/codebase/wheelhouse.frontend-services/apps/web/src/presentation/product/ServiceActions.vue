<script lang="ts">
import type { Product } from "@/domain/products";

/** The per-service controls on a service card and in the service inspector: its pin and its status menu. */
export interface ServiceActionsProps {
  /** The product the service belongs to; without a catalog product nothing can be recorded. */
  readonly product: Product | null;
  /** The service's key in the product's deployment. */
  readonly service: string;
}
</script>
<script setup lang="ts">
import { computed } from "vue";
import { Eye, EyeOff, Ellipsis } from "lucide-vue-next";
import { Button } from "@wow-two-beta/fe/presentation/actions";
import { DropdownMenu, DropdownMenuContent, DropdownMenuTrigger, MenuItem } from "@wow-two-beta/fe/presentation/nav";
import { useProducts } from "@/application/products";
import { PinKind } from "@/domain/pins";
import { ProductStatus, ProductStatusRules } from "@/domain/products";
import { PinToggle } from "@/presentation/pins";

/**
 * A service's pin, which keeps it one click away in every environment, and its status menu: mark it inactive, which
 * hides it until "Show inactive" is on, or active again. Both changes show at once and roll back if the host refuses.
 */
defineOptions({ name: "ServiceActions" });
const props = defineProps<ServiceActionsProps>();
defineSlots<{}>();
const products = useProducts();
const active = computed(() => ProductStatusRules.isServiceActive(props.product, props.service));

/** Records the service's new status. @internal */
function setActive(value: boolean): void {
  if (props.product)
    void products.setServiceStatus(props.product, props.service, value ? ProductStatus.Active : ProductStatus.Inactive);
}
</script>
<template>
  <span v-if="props.product" class="inline-flex items-center gap-0.5">
    <PinToggle
      :subject="{ kind: PinKind.Service, product: props.product, service: props.service }"
      :label="props.service"
    />
    <DropdownMenu placement="bottom-end">
      <DropdownMenuTrigger as-child>
        <Button
          size="sm"
          shape="square"
          variant="ghost"
          tone="neutral"
          class="max-md:size-11"
          :aria-label="`${props.service} options`"
          ><Ellipsis :size="16"
        /></Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent :aria-label="`${props.service} options`" class="w-52">
        <MenuItem v-if="active" @select="setActive(false)"><EyeOff :size="15" />Mark inactive</MenuItem>
        <MenuItem v-else @select="setActive(true)"><Eye :size="15" />Mark active</MenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  </span>
</template>
