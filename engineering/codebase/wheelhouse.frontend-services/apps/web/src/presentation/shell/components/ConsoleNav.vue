<script lang="ts">
/** The console's sidebar navigation, full or as an icon rail. */
export interface ConsoleNavProps {
  /** Shows the navigation as an icon rail: labels stay for assistive technology and as tooltips. */
  readonly collapsed?: boolean;
}
</script>
<script setup lang="ts">
import { computed, defineAsyncComponent } from 'vue';
import { RouterLink, useRoute } from 'vue-router';
import { BookOpen, Boxes, History, House, KeyRound, Plug, Rocket, Server, Settings } from 'lucide-vue-next';
import { DataRegion, SkeletonState } from '@wow-two-beta/fe/presentation/feedback';
import {
  SidebarMenu,
  SidebarMenuGroup,
  SidebarMenuItem,
  SidebarMenuSection,
} from '@wow-two-beta/fe/presentation/nav';
import { ConsolePaths } from '@/application/navigation';
import { usePortfolio } from '@/application/portfolio/usePortfolio';
import { ProductIcon } from '@/presentation/products';
import PinnedNav from './PinnedNav.vue';
import WorkspaceNavGroup from './WorkspaceNavGroup.vue';

/** The switch draws on the SDK's forms bundle, which a first visit otherwise skips; its row keeps its height. @internal */
const ShowInactiveSwitch = defineAsyncComponent(() => import('./ShowInactiveSwitch.vue'));

/**
 * Lists every destination: Home and the portfolio operations first, then the pins in custom order, then the
 * workspaces with their products — the part that grows — and settings at the foot. The rail keeps the same order; a
 * workspace there is one icon that opens its page, and its products show once the sidebar expands.
 */
defineOptions({ name: 'ConsoleNav' });
const props = withDefaults(defineProps<ConsoleNavProps>(), { collapsed: false });
const emit = defineEmits<{ navigate: [] }>();
defineSlots<{}>();
const route = useRoute();
const portfolio = usePortfolio();

/** @internal The fixed destinations, each with its icon: Home and the portfolio-wide operations. */
const Destinations = [
  { path: ConsolePaths.home, label: 'Home', icon: House },
  { path: ConsolePaths.deployments, label: 'Deployments', icon: Rocket },
  { path: ConsolePaths.servers, label: 'Servers', icon: Server },
  { path: ConsolePaths.secrets, label: 'Secrets', icon: KeyRound },
  { path: ConsolePaths.activity, label: 'Activity', icon: History },
] as const;

/** @internal Registry and account settings. */
const SettingsLinks = [
  { path: ConsolePaths.products, label: 'Products', icon: Boxes },
  { path: ConsolePaths.keys, label: 'Integration keys', icon: Plug },
  { path: ConsolePaths.catalog, label: 'Catalog', icon: BookOpen },
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
      <!-- The fixed destinations lead, so they never fall below the fold under the growing workspace tree. -->
      <SidebarMenuItem
        v-for="place in Destinations"
        :key="place.path"
        as-child
        :is-active="isAt(place.path)"
      >
        <template #icon><component :is="place.icon" :size="16" /></template>
        <RouterLink :to="place.path" :title="tip(place.label)" @click="emit('navigate')"
          ><span :class="labelClass">{{ place.label }}</span></RouterLink
        >
      </SidebarMenuItem>

      <PinnedNav :collapsed="props.collapsed" @navigate="emit('navigate')" />

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
                  :is-active="Boolean(group.workspace) && route.params['workspace'] === group.workspace?.slug"
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
                <WorkspaceNavGroup
                  v-for="group in groups"
                  :key="group.key"
                  :group="group"
                  @navigate="emit('navigate')"
                />
              </template>
            </ul>
          </DataRegion>
        </li>
      </SidebarMenuSection>
    </SidebarMenu>

    <div class="shrink-0 border-t border-border pt-3">
      <div v-if="!props.collapsed" class="mb-1 min-h-9"><ShowInactiveSwitch class="min-h-9" /></div>
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
