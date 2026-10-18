<script lang="ts">
/** The console's sidebar navigation, full or as an icon rail. */
export interface ConsoleNavProps {
  /** Shows the navigation as an icon rail: labels stay for assistive technology and as tooltips. */
  readonly collapsed?: boolean;
}
</script>
<script setup lang="ts">
import { computed } from 'vue';
import { RouterLink, useRoute } from 'vue-router';
import { SidebarMenu, SidebarMenuItem } from '@wow-two-beta/fe/presentation/nav';
import { ConsolePaths, primaryDestinationOf } from '@/application/navigation';
import { PrimaryDestinations } from './PrimaryDestinations';
import ProductsNavTree from './ProductsNavTree.vue';

/**
 * Lists the destinations without a heading — Home, Products, Deployments, Servers — with Products as a tree of the
 * products kept one click away: "All products", the product last opened and the pinned ones, each unfolding to its
 * tabs. There is no workspace tree: workspaces live on the Products page. Everything else (activity, vault
 * administration, settings, "Show inactive") sits in the profile menu. The rail keeps the same order, the kept
 * products as icons after Products.
 */
defineOptions({ name: 'ConsoleNav' });
const props = withDefaults(defineProps<ConsoleNavProps>(), { collapsed: false });
const emit = defineEmits<{ navigate: [] }>();
defineSlots<{}>();
const route = useRoute();

/** The primary destination the current page belongs to. @internal */
const current = computed(() => primaryDestinationOf(route.path));

/** The tooltip a rail item shows; the full sidebar shows its label instead. @internal */
function tip(label: string): string | undefined {
  return props.collapsed ? label : undefined;
}

/** The class of a row's label: assistive-only in the rail. @internal */
const labelClass = computed(() => (props.collapsed ? 'sr-only' : 'min-w-0 flex-1 truncate text-start'));
</script>
<template>
  <div class="flex h-full min-h-0 flex-col">
    <SidebarMenu
      aria-label="Console"
      :is-collapsed="props.collapsed"
      class="min-h-0 flex-1 overflow-y-auto overflow-x-hidden [scrollbar-width:thin]"
    >
      <template v-for="place in PrimaryDestinations" :key="place.path">
        <ProductsNavTree
          v-if="place.path === ConsolePaths.products"
          :collapsed="props.collapsed"
          @navigate="emit('navigate')"
        />
        <SidebarMenuItem v-else as-child :is-active="current === place.path">
          <template #icon><component :is="place.icon" :size="16" /></template>
          <RouterLink :to="place.path" :title="tip(place.label)" @click="emit('navigate')"
            ><span :class="labelClass">{{ place.label }}</span></RouterLink
          >
        </SidebarMenuItem>
      </template>
    </SidebarMenu>
  </div>
</template>
