<script lang="ts">
import type { Product } from "@/domain/products";

/** A product's registry controls on its page: its definition and its environments. */
export interface ProductManageMenuProps {
  readonly product: Product;
}
</script>
<script setup lang="ts">
import { ref } from "vue";
import { useRouter } from "vue-router";
import { Pencil, Plus, RefreshCw, Settings2 } from "lucide-vue-next";
import { Button } from "@wow-two-beta/fe/presentation/actions";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuTrigger,
  MenuItem,
  MenuLabel,
  MenuSeparator,
} from "@wow-two-beta/fe/presentation/nav";
import { ConsolePaths } from "@/application/navigation";
import { IconAvailability, useProducts } from "@/application/products";
import { useServers } from "@/application/servers";
import { useTargets } from "@/application/targets";
import { useWorkspaces } from "@/application/workspaces";
import { environmentRank, type InventoryTarget } from "@/domain/targets";
import { failureReason } from "@/integration/common";
import ProductFormModal from "@/presentation/products/components/ProductFormModal.vue";
import TargetFormModal from "@/presentation/products/components/TargetFormModal.vue";

/**
 * Edits the product's definition, reads its manifest again, and adds or edits its environments — the registry work
 * that used to live on the Products page, now beside the product it changes. Deleting the product from its form
 * returns to the products browser.
 */
defineOptions({ name: "ProductManageMenu" });
const props = defineProps<ProductManageMenuProps>();
const emit = defineEmits<{ refreshFailed: [reason: string | null] }>();
defineSlots<{}>();
const router = useRouter();
const products = useProducts();
const workspaces = useWorkspaces();
const servers = useServers();
const targets = useTargets(() => props.product.slug);
const editing = ref(false);
const targetForm = ref<{ open: boolean; target: InventoryTarget | null }>({ open: false, target: null });
const refreshing = ref(false);

/** Opens the environment form: blank to add one, or filled to edit one. */
function openTarget(target: InventoryTarget | null = null): void {
  targetForm.value = { open: true, target };
}

/** Reads the product's manifest again now; a repository GitHub cannot be read for leaves the cached copy and says so. */
async function refreshIdentity(): Promise<void> {
  if (refreshing.value) return;
  refreshing.value = true;
  emit("refreshFailed", null);
  try {
    const result = await products.refreshIdentity(props.product.slug);
    if (!result.ok) emit("refreshFailed", failureReason(result.failure));
    // The repository was read again, so an icon it gained shows: its marks ask for it once more.
    else IconAvailability.forget(props.product.slug);
  } finally {
    refreshing.value = false;
  }
}

/** The product's environments in promotion order. @internal */
function ordered(list: readonly InventoryTarget[]): InventoryTarget[] {
  return [...list].sort((left, right) => environmentRank(left.environment) - environmentRank(right.environment));
}

defineExpose({ addEnvironment: () => openTarget(null) });
</script>
<template>
  <DropdownMenu placement="bottom-end">
    <DropdownMenuTrigger as-child>
      <Button variant="outline" tone="neutral" size="sm" :is-loading="refreshing">
        <template #leading><Settings2 :size="14" /></template>Manage
      </Button>
    </DropdownMenuTrigger>
    <DropdownMenuContent aria-label="Manage the product" class="w-60">
      <MenuItem @select="editing = true"><Pencil :size="15" />Edit product</MenuItem>
      <MenuItem @select="refreshIdentity"><RefreshCw :size="15" />Refresh from repository</MenuItem>
      <MenuSeparator />
      <MenuLabel>Environments</MenuLabel>
      <MenuItem v-for="target in ordered(targets.targets.value)" :key="target.slug" @select="openTarget(target)"
        ><Pencil :size="15" /><span class="min-w-0 flex-1 truncate"
          >Edit {{ target.environment }}<span class="ms-1.5 text-xs text-muted-foreground">{{ target.server }}</span></span
        ></MenuItem
      >
      <MenuItem @select="openTarget(null)"><Plus :size="15" />Add environment</MenuItem>
    </DropdownMenuContent>
  </DropdownMenu>
  <ProductFormModal
    :open="editing"
    :product="props.product"
    :operations="products"
    :workspaces="workspaces.workspaces.value"
    @update:open="editing = $event"
    @removed="router.push(ConsolePaths.products)"
  />
  <TargetFormModal
    :open="targetForm.open"
    :product="props.product.slug"
    :target="targetForm.target"
    :servers="servers.data.value ?? []"
    :operations="targets"
    @update:open="(open) => (targetForm = { ...targetForm, open })"
  />
</template>
