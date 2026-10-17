<script setup lang="ts">
import { computed, defineAsyncComponent, ref, watch } from 'vue';
import { RouterLink, useRoute } from 'vue-router';
import { Boxes, Ellipsis, House, Rocket, Server } from 'lucide-vue-next';
import { BottomNavMenu, BottomNavMenuItem } from '@wow-two-beta/fe/presentation/nav';
import { ConsolePaths } from '@/application/navigation';

/** The sheets load the first time one opens; a first visit ships without them. @internal */
const PhoneSheets = defineAsyncComponent(() => import('./PhoneSheets.vue'));

/**
 * The phone's primary navigation: a bottom bar of five destinations. Products opens the workspaces and their
 * products in a sheet; More holds the remaining places, the "Show inactive" switch and sign-out. The bar sits in the
 * shell's footer row, so the scrolling region ends above it and it never covers content.
 */
defineOptions({ name: 'PhoneNav' });
const emit = defineEmits<{ signOut: [] }>();
defineSlots<{}>();
const route = useRoute();
const productsOpen = ref(false);
const moreOpen = ref(false);
/** True from the first time a sheet opens; until then the sheets' code is not loaded. */
const sheetsWanted = ref(false);
watch([productsOpen, moreOpen], ([products, more]) => {
  if (products || more) sheetsWanted.value = true;
});

/** Whether a destination is the current page, including the pages below it. @internal */
function isAt(path: string): boolean {
  return path === ConsolePaths.home ? route.path === path : route.path === path || route.path.startsWith(`${path}/`);
}

const productsActive = computed(
  () => Boolean(route.params['product'] || route.params['workspace']) || isAt(ConsolePaths.products),
);
const moreActive = computed(
  () => isAt(ConsolePaths.secrets) || isAt(ConsolePaths.activity) || route.path.startsWith('/settings/'),
);

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

  <PhoneSheets
    v-if="sheetsWanted"
    v-model:products="productsOpen"
    v-model:more="moreOpen"
    @sign-out="emit('signOut')"
  />
</template>
