<script lang="ts">
/** The phone's two sheets: the products by workspace, and the remaining places. */
export interface PhoneSheetsProps {
  /** The products sheet's open state — the `v-model:products` target. */
  readonly products: boolean;
  /** The More sheet's open state — the `v-model:more` target. */
  readonly more: boolean;
}
</script>
<script setup lang="ts">
import { RouterLink, useRoute } from 'vue-router';
import { BookOpen, Boxes, History, KeyRound, LayoutGrid, LogOut, Plug } from 'lucide-vue-next';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { DataRegion, SkeletonState } from '@wow-two-beta/fe/presentation/feedback';
import { SidebarMenu, SidebarMenuItem, SidebarMenuSection } from '@wow-two-beta/fe/presentation/nav';
import { BottomSheet, BottomSheetTitle } from '@wow-two-beta/fe/presentation/overlays';
import { ConsolePaths } from '@/application/navigation';
import { usePortfolio } from '@/application/portfolio';
import { ProductStatusRules } from '@/domain/products';
import { ProductIcon } from '@/presentation/products';
import ShowInactiveSwitch from './ShowInactiveSwitch.vue';

/**
 * The sheets the phone's bottom bar opens: Products lists the workspaces and their products; More holds the remaining
 * places, the "Show inactive" switch and sign-out. Loaded the first time either opens, so a first visit ships without
 * them.
 */
defineOptions({ name: 'PhoneSheets' });
const props = defineProps<PhoneSheetsProps>();
const emit = defineEmits<{ 'update:products': [open: boolean]; 'update:more': [open: boolean]; signOut: [] }>();
defineSlots<{}>();
const route = useRoute();
const portfolio = usePortfolio();

/** @internal The places under More. */
const MorePlaces = [
  { path: ConsolePaths.secrets, label: 'Secrets', icon: KeyRound },
  { path: ConsolePaths.activity, label: 'Activity', icon: History },
  { path: ConsolePaths.keys, label: 'Integration keys', icon: Plug },
  { path: ConsolePaths.catalog, label: 'Catalog', icon: BookOpen },
] as const;

/** Whether a place is the current page, including the pages below it. @internal */
function isAt(path: string): boolean {
  return route.path === path || route.path.startsWith(`${path}/`);
}
</script>
<template>
  <BottomSheet :open="props.products" @update:open="emit('update:products', $event)" :snap-points="['70vh', '92vh']">
    <BottomSheetTitle class="mb-3 px-1 text-base font-semibold">Products</BottomSheetTitle>
    <DataRegion
      :is-first-load="portfolio.isFirstLoad.value"
      :error="portfolio.error.value"
      :has-data="portfolio.hasData.value"
      loading-label="Loading products"
    >
      <template #skeleton>
        <div class="flex flex-col gap-2" aria-hidden="true">
          <SkeletonState v-for="index in 4" :key="index" class="h-11 w-full rounded-lg" />
        </div>
      </template>
      <SidebarMenu aria-label="Products by workspace" class="pb-2">
        <SidebarMenuSection
          v-for="group in portfolio.groups.value"
          :key="group.workspace?.id ?? 'none'"
          :label="group.workspace?.name ?? 'No workspace'"
        >
          <SidebarMenuItem v-if="group.workspace" as-child class="h-11">
            <template #icon><LayoutGrid :size="16" /></template>
            <RouterLink :to="ConsolePaths.workspace(group.workspace.slug)"
              ><span class="min-w-0 flex-1 truncate">All in {{ group.workspace.name }}</span
              ><span class="text-xs tabular-nums text-muted-foreground">{{ group.products.length }}</span></RouterLink
            >
          </SidebarMenuItem>
          <SidebarMenuItem
            v-for="product in group.products"
            :key="product.slug"
            as-child
            class="h-11"
            :is-active="route.params.product === product.slug"
          >
            <template #icon><ProductIcon :product-slug="product.slug" :name="product.name" size="xs" /></template>
            <RouterLink
              :to="ConsolePaths.product(product.slug)"
              :class="ProductStatusRules.isActive(product) ? '' : 'opacity-60'"
              ><span class="min-w-0 flex-1 truncate">{{ product.name }}</span></RouterLink
            >
          </SidebarMenuItem>
        </SidebarMenuSection>
      </SidebarMenu>
    </DataRegion>
    <div class="border-t border-border pt-3">
      <Button as-child variant="ghost" tone="neutral" is-full-width class="h-11 justify-start">
        <RouterLink :to="ConsolePaths.products"><Boxes :size="16" />Manage products and workspaces</RouterLink>
      </Button>
    </div>
  </BottomSheet>

  <BottomSheet :open="props.more" @update:open="emit('update:more', $event)" :snap-points="[390, '85vh']">
    <BottomSheetTitle class="sr-only">More</BottomSheetTitle>
    <SidebarMenu aria-label="More">
      <SidebarMenuItem v-for="place in MorePlaces" :key="place.path" as-child class="h-11" :is-active="isAt(place.path)">
        <template #icon><component :is="place.icon" :size="18" /></template>
        <RouterLink :to="place.path">{{ place.label }}</RouterLink>
      </SidebarMenuItem>
    </SidebarMenu>
    <div class="mt-2 border-t border-border pt-2">
      <ShowInactiveSwitch class="min-h-11" />
      <Button variant="ghost" tone="danger" is-full-width class="mt-1 h-11 justify-start" @click="emit('signOut')">
        <template #leading><LogOut :size="18" /></template>Sign out
      </Button>
    </div>
  </BottomSheet>
</template>
