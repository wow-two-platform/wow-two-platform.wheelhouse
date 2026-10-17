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
import { DataRegion, SkeletonState } from '@wow-two-beta/fe/presentation/feedback';
import { SidebarMenu, SidebarMenuItem, SidebarMenuSection } from '@wow-two-beta/fe/presentation/nav';
import { ConsolePaths, primaryDestinationOf } from '@/application/navigation';
import { usePortfolio } from '@/application/portfolio/usePortfolio';
import { ProductIcon } from '@/presentation/products';
import PinnedNav from './PinnedNav.vue';
import { PrimaryDestinations } from './PrimaryDestinations';
import WorkspaceNavGroup from './WorkspaceNavGroup.vue';

/**
 * Lists the destinations: Home and the entities — Products, Deployments, Servers — without a heading, then the pins
 * in custom order, then the workspaces with their products, the part that grows. Everything else (activity, vault
 * administration, settings, "Show inactive") sits in the profile menu. The rail keeps the same order; a workspace
 * there is one icon that opens its products, and its products show once the sidebar expands.
 */
defineOptions({ name: 'ConsoleNav' });
const props = withDefaults(defineProps<ConsoleNavProps>(), { collapsed: false });
const emit = defineEmits<{ navigate: [] }>();
defineSlots<{}>();
const route = useRoute();
const portfolio = usePortfolio();

/** Each workspace group's key, stable across reads. @internal */
const groups = computed(() =>
  portfolio.groups.value.map((group) => ({ ...group, key: group.workspace?.id ?? 'none' })),
);
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
      <!-- The fixed destinations lead, so they never fall below the fold under the growing workspace tree. -->
      <SidebarMenuItem
        v-for="place in PrimaryDestinations"
        :key="place.path"
        as-child
        :is-active="current === place.path"
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
                <!-- The rail has no room for nested rows: each workspace is one icon opening its products. -->
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
  </div>
</template>
