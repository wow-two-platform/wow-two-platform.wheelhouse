<script lang="ts">
import type { Product } from "@/domain/products";

/** A product's management menu on its page: status, definition, environments and repository. */
export interface ProductManageMenuProps {
  readonly product: Product;
  /** The runner catalog product for the repository; the clone dialog lists branches and commits only with one. */
  readonly runnerProduct?: string | null;
}
</script>
<script setup lang="ts">
import { computed, ref } from "vue";
import { useRouter } from "vue-router";
import { Copy, Github, Pencil, Plus, RefreshCw, Settings2 } from "lucide-vue-next";
import { Button } from "@wow-two-beta/fe/presentation/actions";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuTrigger,
  MenuCheckboxItem,
  MenuItem,
  MenuLabel,
  MenuSeparator,
} from "@wow-two-beta/fe/presentation/nav";
import { ConsolePaths } from "@/application/navigation";
import { IconAvailability, useProductDescriptor, useProducts } from "@/application/products";
import { useServers } from "@/application/servers";
import { useTargets } from "@/application/targets";
import { knownSitesOf } from "@/application/targets/TargetForms";
import { useWorkspaces } from "@/application/workspaces";
import { ProductDescriptorRules, ProductStatus, ProductStatusRules } from "@/domain/products";
import { environmentRank, type InventoryTarget } from "@/domain/targets";
import { failureReason } from "@/integration/common";
import { useIsPhone } from "@/presentation/deployments/hooks/useIsPhone";
import CloneModal from "@/presentation/products/components/CloneModal.vue";
import ProductFormModal from "@/presentation/products/components/ProductFormModal.vue";
import TargetFormModal from "@/presentation/products/components/TargetFormModal.vue";

/**
 * Everything that changes or reaches the product beyond its tabs, in one menu: whether it is active (inactive hides
 * it until "Show inactive" is on; the change shows at once and rolls back if refused), editing its definition,
 * reading its manifest again, adding or editing its environments, and its repository — open on GitHub, or the clone
 * dialog with its formats, branches and commits. Deleting the product from its form returns to the products grid.
 * Phones show the trigger as an icon.
 */
defineOptions({ name: "ProductManageMenu" });
const props = withDefaults(defineProps<ProductManageMenuProps>(), { runnerProduct: null });
const emit = defineEmits<{ refreshFailed: [reason: string | null] }>();
defineSlots<{}>();
const router = useRouter();
const products = useProducts();
const workspaces = useWorkspaces();
const servers = useServers();
const targets = useTargets(() => props.product.slug);
const isPhone = useIsPhone();
const editing = ref(false);
const cloning = ref(false);
const targetForm = ref<{ open: boolean; target: InventoryTarget | null }>({ open: false, target: null });
const refreshing = ref(false);
const active = computed(() => ProductStatusRules.isActive(props.product));
const repositoryUrl = computed(() => props.product.repository.url || `https://github.com/${props.product.repository.name}`);
const descriptor = useProductDescriptor(() => props.product.slug);
const knownSites = computed(() => knownSitesOf(props.product, descriptor.data.value));
/** The services every environment runs: deploy.yml's, else the ones the release source was derived from. */
const services = computed(() => {
  const read = descriptor.data.value;
  return read && !read.problem
    ? ProductDescriptorRules.serviceNames(read)
    : (props.product.release?.images.map((image) => image.service) ?? []);
});

/** Opens the environment form: blank to add one, or filled to edit one. */
function openTarget(target: InventoryTarget | null = null): void {
  targetForm.value = { open: true, target };
}

/** Marks the product active or inactive. @internal */
function setActive(value: boolean): void {
  void products.setStatus(props.product, value ? ProductStatus.Active : ProductStatus.Inactive);
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

/** Opens the repository in a new tab. @internal */
function openRepository(): void {
  globalThis.open?.(repositoryUrl.value, "_blank", "noopener,noreferrer");
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
      <Button
        variant="outline"
        tone="neutral"
        size="sm"
        :shape="isPhone ? 'square' : 'default'"
        :class="isPhone ? 'size-11' : ''"
        :is-loading="refreshing"
        :aria-label="isPhone ? `Manage ${props.product.name}` : undefined"
        :title="`Manage ${props.product.name}`"
      >
        <template v-if="!isPhone" #leading><Settings2 :size="14" /></template
        ><Settings2 v-if="isPhone" :size="16" /><template v-else>Manage</template>
      </Button>
    </DropdownMenuTrigger>
    <DropdownMenuContent :aria-label="`Manage ${props.product.name}`" class="w-64">
      <MenuCheckboxItem :model-value="active" @update:model-value="setActive">Active</MenuCheckboxItem>
      <MenuSeparator />
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
      <MenuSeparator />
      <MenuLabel>Repository</MenuLabel>
      <MenuItem @select="openRepository"
        ><Github :size="15" /><span class="min-w-0 flex-1 truncate">{{ props.product.repository.name }}</span></MenuItem
      >
      <MenuItem @select="cloning = true"><Copy :size="15" />Clone…</MenuItem>
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
    :known-sites="knownSites"
    :services="services"
    @update:open="(open) => (targetForm = { ...targetForm, open })"
  />
  <CloneModal
    v-model:open="cloning"
    :repository="props.product.repository.name"
    :runner-product="props.runnerProduct"
  />
</template>
