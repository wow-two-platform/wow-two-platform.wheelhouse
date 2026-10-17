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
import { Boxes, History, House, KeyRound, LayoutGrid, Rocket, Server, Settings } from 'lucide-vue-next';
import { DataRegion, SkeletonState } from '@wow-two-beta/fe/presentation/feedback';
import {
  SidebarMenu,
  SidebarMenuGroup,
  SidebarMenuItem,
  SidebarMenuSection,
} from '@wow-two-beta/fe/presentation/nav';
import { ConsolePaths } from '@/application/navigation';
import { usePortfolio } from '@/application/portfolio';
import { ProductStatusRules } from '@/domain/products';
import { ProductIcon } from '@/presentation/products';
import ShowInactiveSwitch from './ShowInactiveSwitch.vue';

/**
 * Lists every destination: Home, the workspaces with their products, the portfolio operations and settings. In the
 * rail a workspace is one icon that opens its page; its products show once the sidebar expands.
 */
defineOptions({ name: 'ConsoleNav' });
const props = withDefaults(defineProps<ConsoleNavProps>(), { collapsed: false });
const emit = defineEmits<{ navigate: [] }>();
defineSlots<{}>();
const route = useRoute();
const portfolio = usePortfolio();

/** @internal The portfolio-wide operations, each with its icon. */
const Operations = [
  { path: ConsolePaths.deployments, label: 'Deployments', icon: Rocket },
  { path: ConsolePaths.servers, label: 'Servers', icon: Server },
  { path: ConsolePaths.secrets, label: 'Secrets', icon: KeyRound },
  { path: ConsolePaths.activity, label: 'Activity', icon: History },
] as const;

/** @internal Registry and account settings. */
const SettingsLinks = [
  { path: ConsolePaths.products, label: 'Products', icon: Boxes },
  { path: ConsolePaths.keys, label: 'Integration keys', icon: KeyRound },
] as const;

/** Each workspace group's key, stable across reads. @internal */
const groups = computed(() =>
  portfolio.groups.value.map((group) => ({ ...group, key: group.workspace?.id ?? 'none' })),
);

/** Whether a fixed destination is the current page, including the pages below it. @internal */
function isAt(path: string): boolean {
  return path === ConsolePaths.home ? route.path === path : route.path === path || route.path.startsWith(`${path}/`);
}

/** The tooltip a rail item shows; the full sidebar shows its label instead. @internal */
function tip(label: string): string | undefined {
  return props.collapsed ? label : undefined;
}

/** The class of a row's label: assistive-only in the rail. @internal */
const labelClass = computed(() => (props.collapsed ? 'sr-only' : 'min-w-0 flex-1 truncate text-start'));
</script>
<template>
  <div class="flex h-full min-h-0 flex-col gap-3">
    <SidebarMenu
      aria-label="Console"
      :is-collapsed="props.collapsed"
      class="min-h-0 flex-1 overflow-y-auto overflow-x-hidden [scrollbar-width:thin]"
    >
      <SidebarMenuItem as-child :is-active="isAt(ConsolePaths.home)">
        <template #icon><House :size="16" /></template>
        <RouterLink :to="ConsolePaths.home" :title="tip('Home')" @click="emit('navigate')"
          ><span :class="labelClass">Home</span></RouterLink
        >
      </SidebarMenuItem>

      <SidebarMenuSection label="Workspaces">
        <li>
          <DataRegion
            :is-first-load="portfolio.isFirstLoad.value"
            :error="portfolio.error.value"
            :has-data="portfolio.hasData.value"
            loading-label="Loading workspaces"
          >
            <template #skeleton>
              <div class="flex flex-col gap-1.5 py-1" aria-hidden="true">
                <SkeletonState v-for="index in 3" :key="index" class="h-8 w-full rounded-md" />
              </div>
            </template>
            <template #unavailable>
              <p class="px-2.5 py-2 text-xs text-muted-foreground">{{ props.collapsed ? '' : 'Products unavailable' }}</p>
            </template>
            <ul role="list" class="flex flex-col gap-0.5">
              <template v-if="props.collapsed">
                <!-- The rail has no room for nested rows: each workspace is one icon opening its page. -->
                <SidebarMenuItem
                  v-for="group in groups"
                  :key="group.key"
                  as-child
                  :is-active="Boolean(group.workspace) && route.params.workspace === group.workspace?.slug"
                >
                  <template #icon
                    ><ProductIcon :name="group.workspace?.name ?? 'No workspace'" size="xs"
                  /></template>
                  <RouterLink
                    :to="group.workspace ? ConsolePaths.workspace(group.workspace.slug) : ConsolePaths.products"
                    :title="tip(group.workspace?.name ?? 'No workspace')"
                    @click="emit('navigate')"
                    ><span :class="labelClass">{{ group.workspace?.name ?? 'No workspace' }}</span></RouterLink
                  >
                </SidebarMenuItem>
              </template>
              <template v-else>
                <SidebarMenuGroup
                  v-for="group in groups"
                  :key="group.key"
                  :label="group.workspace?.name ?? 'No workspace'"
                  :default-open="true"
                >
                  <template #icon><ProductIcon :name="group.workspace?.name ?? 'No workspace'" size="xs" /></template>
                  <SidebarMenuItem
                    v-if="group.workspace"
                    as-child
                    :is-active="route.params.workspace === group.workspace.slug"
                  >
                    <template #icon><LayoutGrid :size="14" /></template>
                    <RouterLink :to="ConsolePaths.workspace(group.workspace.slug)" @click="emit('navigate')"
                      ><span :class="labelClass">Overview</span
                      ><span class="text-xs tabular-nums text-muted-foreground">{{ group.products.length }}</span></RouterLink
                    >
                  </SidebarMenuItem>
                  <SidebarMenuItem
                    v-for="product in group.products"
                    :key="product.slug"
                    as-child
                    :is-active="route.params.product === product.slug"
                  >
                    <template #icon><ProductIcon :product-slug="product.slug" :name="product.name" size="xs" /></template>
                    <RouterLink
                      :to="ConsolePaths.product(product.slug)"
                      :class="ProductStatusRules.isActive(product) ? '' : 'opacity-60'"
                      @click="emit('navigate')"
                      ><span :class="labelClass">{{ product.name }}</span></RouterLink
                    >
                  </SidebarMenuItem>
                  <li v-if="!group.products.length" class="px-2.5 py-1.5 text-xs text-muted-foreground">
                    {{ group.total ? 'Only inactive products' : 'No products yet' }}
                  </li>
                </SidebarMenuGroup>
              </template>
            </ul>
          </DataRegion>
        </li>
      </SidebarMenuSection>

      <SidebarMenuSection label="Operations">
        <SidebarMenuItem v-for="place in Operations" :key="place.path" as-child :is-active="isAt(place.path)">
          <template #icon><component :is="place.icon" :size="16" /></template>
          <RouterLink :to="place.path" :title="tip(place.label)" @click="emit('navigate')"
            ><span :class="labelClass">{{ place.label }}</span></RouterLink
          >
        </SidebarMenuItem>
      </SidebarMenuSection>
    </SidebarMenu>

    <div class="shrink-0 border-t border-border pt-3">
      <ShowInactiveSwitch v-if="!props.collapsed" class="mb-1 min-h-9" />
      <SidebarMenu aria-label="Settings" :is-collapsed="props.collapsed">
        <SidebarMenuGroup v-if="!props.collapsed" label="Settings" :default-open="SettingsLinks.some((link) => isAt(link.path))">
          <template #icon><Settings :size="16" /></template>
          <SidebarMenuItem v-for="link in SettingsLinks" :key="link.path" as-child :is-active="isAt(link.path)">
            <template #icon><component :is="link.icon" :size="14" /></template>
            <RouterLink :to="link.path" @click="emit('navigate')"
              ><span :class="labelClass">{{ link.label }}</span></RouterLink
            >
          </SidebarMenuItem>
        </SidebarMenuGroup>
        <template v-else>
          <SidebarMenuItem v-for="link in SettingsLinks" :key="link.path" as-child :is-active="isAt(link.path)">
            <template #icon><component :is="link.icon" :size="16" /></template>
            <RouterLink :to="link.path" :title="tip(link.label)" @click="emit('navigate')"
              ><span :class="labelClass">{{ link.label }}</span></RouterLink
            >
          </SidebarMenuItem>
        </template>
      </SidebarMenu>
    </div>
  </div>
</template>
