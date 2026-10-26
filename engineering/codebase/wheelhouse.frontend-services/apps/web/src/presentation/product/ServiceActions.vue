<script lang="ts">
import type { Product } from "@/domain/products";
import type { ControlTarget, TargetStop } from "@/domain/deployments/controls";

/** The per-service controls on a service card and in the service inspector: its status menu. */
export interface ServiceActionsProps {
  /** The product the service belongs to; without a catalog product nothing can be recorded. */
  readonly product: Product | null;
  /** The service's key in the product's deployment. */
  readonly service: string;
  /** The environment the service runs in, which its stop, start and restart act on; none offers no control. */
  readonly target?: ControlTarget | null;
  /** What the owner keeps stopped on that environment. */
  readonly stopped?: TargetStop | null;
}
</script>
<script setup lang="ts">
import { computed, ref } from "vue";
import { Eye, EyeOff, Ellipsis, Pause, Play, RotateCw } from "lucide-vue-next";
import { Button } from "@wow-two-beta/fe/presentation/actions";
import { DropdownMenu, DropdownMenuContent, DropdownMenuTrigger, MenuItem } from "@wow-two-beta/fe/presentation/nav";
import { useProducts } from "@/application/products";
import { ControlOperation, ServiceControls, type ControlRequest } from "@/domain/deployments/controls";
import { SelfProduct } from "@/domain/deployments";
import { ProductStatus, ProductStatusRules } from "@/domain/products";
import ControlConfirmModal from "@/presentation/deployments/components/ControlConfirmModal.vue";

/**
 * A service's menu: stop, start or restart it in the environment shown (never on Wheelhouse itself), and mark it
 * inactive, which hides it until "Show inactive" is on, or active again. A control asks for confirmation first; the
 * status change shows at once and rolls back if the host refuses it. Services are not pinned; their product is.
 */
defineOptions({ name: "ServiceActions" });
const props = defineProps<ServiceActionsProps>();
defineSlots<{}>();
const products = useProducts();
const active = computed(() => ProductStatusRules.isServiceActive(props.product, props.service));
const controls = computed(() =>
  props.target && ServiceControls.offers(props.target.product, SelfProduct)
    ? ServiceControls.forService(props.stopped, props.service)
    : [],
);
const request = ref<ControlRequest | null>(null);
const confirming = ref(false);

/** Records the service's new status. @internal */
function setActive(value: boolean): void {
  if (props.product)
    void products.setServiceStatus(props.product, props.service, value ? ProductStatus.Active : ProductStatus.Inactive);
}

/** Asks to confirm one control of the service. @internal */
function ask(operation: ControlOperation): void {
  if (!props.target) return;
  request.value = { target: props.target, operation, service: props.service };
  confirming.value = true;
}
</script>
<template>
  <span v-if="props.product" class="inline-flex items-center gap-0.5">
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
        <MenuItem v-for="operation in controls" :key="operation" @select="ask(operation)"
          ><Play v-if="operation === ControlOperation.Start" :size="15" /><RotateCw
            v-else-if="operation === ControlOperation.Restart"
            :size="15" /><Pause v-else :size="15" />{{ ServiceControls.verb(operation) }}</MenuItem
        >
        <MenuItem v-if="active" @select="setActive(false)"><EyeOff :size="15" />Mark inactive</MenuItem>
        <MenuItem v-else @select="setActive(true)"><Eye :size="15" />Mark active</MenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
    <ControlConfirmModal :open="confirming" :request="request" @update:open="confirming = $event" />
  </span>
</template>
