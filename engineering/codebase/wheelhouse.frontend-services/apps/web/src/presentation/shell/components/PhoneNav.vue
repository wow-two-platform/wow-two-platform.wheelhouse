<script setup lang="ts">
import { computed } from 'vue';
import { RouterLink, useRoute } from 'vue-router';
import { BottomNavMenu, BottomNavMenuItem } from '@wow-two-beta/fe/presentation/nav';
import { primaryDestinationOf } from '@/application/navigation';
import { PrimaryDestinations } from './PrimaryDestinations';

/**
 * The phone's primary navigation: a bottom bar of the four destinations the sidebar leads with — Home, Products,
 * Deployments, Servers. Everything else opens from the profile menu in the top bar. The bar sits in the shell's footer
 * row, so the scrolling region ends above it and it never covers content.
 */
defineOptions({ name: 'PhoneNav' });
defineSlots<{}>();
const route = useRoute();
/** The destination the current page belongs to; a workspace or a product page counts as Products. @internal */
const current = computed(() => primaryDestinationOf(route.path));
</script>
<template>
  <BottomNavMenu
    :is-fixed="false"
    aria-label="Primary"
    class="wh-glass z-sticky border-(--wh-glass-edge) [grid-area:footer] md:hidden"
  >
    <BottomNavMenuItem
      v-for="place in PrimaryDestinations"
      :key="place.path"
      as-child
      :is-active="current === place.path"
    >
      <template #icon><component :is="place.icon" :size="20" /></template>
      <RouterLink :to="place.path">{{ place.label }}</RouterLink>
    </BottomNavMenuItem>
  </BottomNavMenu>
</template>
