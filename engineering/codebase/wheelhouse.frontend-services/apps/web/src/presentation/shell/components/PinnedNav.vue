<script lang="ts">
/** The sidebar's pinned products, below the main menu. */
export interface PinnedNavProps {
  /** The icon rail: a divider, then each pinned product as an icon with its status badge. */
  readonly collapsed?: boolean;
}
</script>
<script setup lang="ts">
import { computed, defineAsyncComponent, ref } from "vue";
import { RouterLink } from "vue-router";
import { Check, Pencil } from "lucide-vue-next";
import { SkeletonState } from "@wow-two-beta/fe/presentation/feedback";
import { SidebarMenuItem, SidebarMenuSection } from "@wow-two-beta/fe/presentation/nav";
import { ConsolePaths } from "@/application/navigation";
import { usePinned } from "@/application/pins";
import { ProductStatusRules } from "@/domain/products";
import { ProductIcon } from "@/presentation/products";
import { useNavConditions } from "../hooks/useNavConditions";
import ProductStatusMark from "./ProductStatusMark.vue";

/** The editor loads when reordering starts; it draws on the SDK's sortable list, which browsing never needs. @internal */
const PinEditor = defineAsyncComponent(() => import("@/presentation/pins/PinEditor.vue"));

/**
 * The pinned products under a divider, in the owner's order: each its mark, its name and a status mark only when its
 * latest rollouts need the owner — failing, needing a look, or deploying. Reorder edits the order and unpins in
 * place. The rail shows each as an icon with its status badge; with nothing pinned the rail shows no section.
 */
defineOptions({ name: "PinnedNav" });
const props = withDefaults(defineProps<PinnedNavProps>(), { collapsed: false });
const emit = defineEmits<{ navigate: [] }>();
defineSlots<{}>();
const pinned = usePinned();
const conditions = useNavConditions();
const editing = ref(false);

const products = computed(() => pinned.visible.value.flatMap((entry) => (entry.product ? [entry.product] : [])));
const isEditing = computed(() => editing.value && !props.collapsed);
const isShown = computed(() => !props.collapsed || pinned.isFirstLoad.value || products.value.length > 0);
</script>
<template>
  <SidebarMenuSection v-if="isShown" label="Pinned" class="mt-3 border-t border-border pt-3">
    <li v-if="pinned.isFirstLoad.value" aria-hidden="true" class="flex flex-col gap-1.5 py-1">
      <SkeletonState v-for="index in 2" :key="index" class="h-8 w-full rounded-md" />
    </li>
    <li v-else-if="isEditing" class="py-1">
      <PinEditor :entries="pinned.entries.value" compact />
    </li>
    <template v-else>
      <SidebarMenuItem v-for="product in products" :key="product.slug" as-child>
        <template #icon
          ><span class="relative inline-flex"
            ><ProductIcon :product-slug="product.slug" :name="product.name" size="xs" /><ProductStatusMark
              v-if="props.collapsed"
              :condition="conditions.get(product.slug)"
              is-badge /></span
        ></template>
        <template #trailing><ProductStatusMark :condition="conditions.get(product.slug)" /></template>
        <RouterLink
          :to="ConsolePaths.product(product.slug)"
          :title="product.name"
          :class="ProductStatusRules.isActive(product) ? '' : 'opacity-60'"
          @click="emit('navigate')"
          ><span :class="props.collapsed ? 'sr-only' : 'min-w-0 flex-1 truncate text-start'">{{ product.name }}</span></RouterLink
        >
      </SidebarMenuItem>
      <li v-if="!products.length && !props.collapsed" class="px-2.5 py-1.5 text-xs text-muted-foreground">
        Pin a product from its menu to keep it here.
      </li>
    </template>
    <li v-if="!props.collapsed && (pinned.entries.value.length > 1 || (isEditing && pinned.entries.value.length))" class="pt-0.5">
      <button
        type="button"
        class="inline-flex h-7 items-center gap-1.5 rounded-md px-2.5 text-xs font-medium text-muted-foreground hover:bg-muted hover:text-foreground focus-visible:outline-2 focus-visible:outline-ring"
        :aria-pressed="isEditing"
        @click="editing = !editing"
      >
        <Check v-if="isEditing" :size="12" /><Pencil v-else :size="12" />{{ isEditing ? "Done" : "Reorder pins" }}
      </button>
    </li>
  </SidebarMenuSection>
</template>
