<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { RouterLink, useRoute } from 'vue-router';
import { BookOpen, Boxes, Plug, Ellipsis, History, House, KeyRound, LayoutGrid, LogOut, Rocket, Server } from 'lucide-vue-next';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { DataRegion, SkeletonState } from '@wow-two-beta/fe/presentation/feedback';
import {
  BottomNavMenu,
  BottomNavMenuItem,
  SidebarMenu,
  SidebarMenuItem,
  SidebarMenuSection,
} from '@wow-two-beta/fe/presentation/nav';
import { BottomSheet, BottomSheetTitle } from '@wow-two-beta/fe/presentation/overlays';
import { ConsolePaths } from '@/application/navigation';
import { usePortfolio } from '@/application/portfolio';
import { ProductStatusRules } from '@/domain/products';
import { ProductIcon } from '@/presentation/products';
import ShowInactiveSwitch from './ShowInactiveSwitch.vue';

/**
 * The phone's primary navigation: a bottom bar of five destinations. Products opens the workspaces and their
 * products in a sheet; More holds the remaining places, the "Show inactive" switch and sign-out. The bar sits in the
 * shell's footer row, so the scrolling region ends above it and it never covers content.
 */
defineOptions({ name: 'PhoneNav' });
const emit = defineEmits<{ signOut: [] }>();
defineSlots<{}>();
const route = useRoute();
const portfolio = usePortfolio();
const productsOpen = ref(false);
const moreOpen = ref(false);

/** @internal The places under More. */
const MorePlaces = [
  { path: ConsolePaths.secrets, label: 'Secrets', icon: KeyRound },
  { path: ConsolePaths.activity, label: 'Activity', icon: History },
  { path: ConsolePaths.keys, label: 'Integration keys', icon: Plug },
  { path: ConsolePaths.catalog, label: 'Catalog', icon: BookOpen },
] as const;

/** Whether a destination is the current page, including the pages below it. @internal */
function isAt(path: string): boolean {
  return path === ConsolePaths.home ? route.path === path : route.path === path || route.path.startsWith(`${path}/`);
}

const productsActive = computed(
  () => Boolean(route.params.product || route.params.workspace) || isAt(ConsolePaths.products),
);
const moreActive = computed(() => MorePlaces.some((place) => isAt(place.path)) || route.path.startsWith('/settings/'));

/* Any navigation closes both sheets. */
watch(
  () => route.fullPath,
  () => {
    productsOpen.value = false;
    moreOpen.value = false;
  },
);
</script>
<template>
  <BottomNavMenu
    :is-fixed="false"
    aria-label="Primary"
    class="wh-glass z-sticky border-(--wh-glass-edge) [grid-area:footer] md:hidden"
  >
    <BottomNavMenuItem as-child :is-active="isAt(ConsolePaths.home)">
      <template #icon><House :size="20" /></template>
      <RouterLink :to="ConsolePaths.home">Home</RouterLink>
    </BottomNavMenuItem>
    <BottomNavMenuItem as-child :is-active="productsActive">
      <template #icon><Boxes :size="20" /></template>
      <button type="button" aria-haspopup="dialog" @click="productsOpen = true">Products</button>
    </BottomNavMenuItem>
    <BottomNavMenuItem as-child :is-active="isAt(ConsolePaths.deployments)">
      <template #icon><Rocket :size="20" /></template>
      <RouterLink :to="ConsolePaths.deployments">Deployments</RouterLink>
    </BottomNavMenuItem>
    <BottomNavMenuItem as-child :is-active="isAt(ConsolePaths.servers)">
      <template #icon><Server :size="20" /></template>
      <RouterLink :to="ConsolePaths.servers">Servers</RouterLink>
    </BottomNavMenuItem>
    <BottomNavMenuItem as-child :is-active="moreActive">
      <template #icon><Ellipsis :size="20" /></template>
      <button type="button" aria-haspopup="dialog" @click="moreOpen = true">More</button>
    </BottomNavMenuItem>
  </BottomNavMenu>

  <BottomSheet v-model:open="productsOpen" :snap-points="['70vh', '92vh']">
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

  <BottomSheet v-model:open="moreOpen" :snap-points="[390, '85vh']">
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
