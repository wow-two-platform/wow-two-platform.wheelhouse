<script lang="ts">
import type { CurrentUser } from '@/domain/auth';

/** Defines the authenticated console frame. */
export interface AppLayoutProps {
  readonly user: CurrentUser;
}

/** @internal The sidebar's widths: full, and the icon rail. */
const SidebarWidth = { Full: '248px', Rail: '64px' } as const;
</script>
<script setup lang="ts">
import { computed, defineAsyncComponent, ref, useTemplateRef, watch } from 'vue';
import { RouterLink, useRoute } from 'vue-router';
import { PanelLeft } from 'lucide-vue-next';
import { TailwindBreakpoints, useBreakpoint } from '@wow-two-beta/fe/foundation/device';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { Badge } from '@wow-two-beta/fe/presentation/display';
import {
  AppShell,
  AppShellContent,
  AppShellHeader,
  AppShellMain,
  AppShellSidebar,
} from '@wow-two-beta/fe/presentation/layout';
import { scopeOf } from '@/application/navigation';
import { usePortfolio } from '@/application/portfolio/usePortfolio';
import { useSidebarCollapsed } from '@/application/preferences';
import { useApiConnection } from '@/application/system';
import {
  BrandMark,
  BrandWordmark,
  ConsoleNav,
  PhoneNav,
  ProfileMenu,
  ScopeBar,
} from '@/presentation/shell';

/** The tablet's drawer loads the first time it opens; desktops and phones never need it. @internal */
const NavDrawer = defineAsyncComponent(() => import('@/presentation/shell/components/NavDrawer.vue'));

/**
 * The console frame. Desktop: a sidebar that collapses to an icon rail, remembered across visits. Tablet: the rail,
 * with the full sidebar in an overlay drawer. Phone: no sidebar; a bottom bar in the shell's footer row. The top bar
 * names the scope and carries the page's actions on every class.
 */
defineOptions({ name: 'AppLayout' });
const props = defineProps<AppLayoutProps>();
const emit = defineEmits<{ signOut: [] }>();
defineSlots<{ default(): unknown }>();
const route = useRoute();
const system = useApiConnection();
const portfolio = usePortfolio();
const collapsed = useSidebarCollapsed();
const breakpoint = useBreakpoint(TailwindBreakpoints);
const drawerOpen = ref(false);
/** True from the first time the drawer opens; until then its code is not loaded. */
const drawerWanted = ref(false);
const consoleVersion = __APP_VERSION__;
/** A numbered build shows as `v0.3.4`; a local build shows `dev`. @internal */
const versionLabel = /^\d/.test(consoleVersion) ? `v${consoleVersion}` : consoleVersion;
const main = useTemplateRef<{ el: HTMLElement | null }>('main');

/** At least `lg`: the sidebar sits beside the content and can collapse. */
const isDesktop = computed(() => breakpoint.value === 'lg' || breakpoint.value === 'xl' || breakpoint.value === '2xl');
/** The sidebar shows as the icon rail: always on tablets, by choice on desktops. */
const isRail = computed(() => !isDesktop.value || collapsed.value);
const sidebarWidth = computed(() => (isRail.value ? SidebarWidth.Rail : SidebarWidth.Full));

const crumbs = computed(() =>
  scopeOf({
    path: route.path,
    params: { workspace: param(route.params['workspace']), product: param(route.params['product']) },
    query: { env: query('env'), inspect: query('inspect'), item: query('item') },
    title: typeof route.meta['title'] === 'string' ? route.meta['title'] : null,
    workspaces: portfolio.workspaces.workspaces.value,
    products: portfolio.products.products.value,
  }),
);

/** Reads a path parameter, or null without one. @internal */
function param(value: unknown): string | null {
  const item = Array.isArray(value) ? value[0] : value;
  return typeof item === 'string' && item ? item : null;
}
/** Reads one query value, or null without one. @internal */
function query(key: string): string | null {
  const value = route.query[key];
  const item = Array.isArray(value) ? value[0] : value;
  return typeof item === 'string' ? item : null;
}

/** Desktop: collapse or expand the sidebar. Tablet: open the full sidebar over the content. @internal */
function toggleSidebar(): void {
  if (isDesktop.value) collapsed.value = !collapsed.value;
  else {
    drawerWanted.value = true;
    drawerOpen.value = true;
  }
}

/* The main region is the scroll container, so a new page starts at its top. A selection inside a page (a query
   change) keeps its place. */
watch(
  () => route.path,
  () => {
    main.value?.el?.scrollTo({ top: 0 });
    drawerOpen.value = false;
  },
);
/* Growing to desktop closes the tablet's drawer; the sidebar is beside the content again. */
watch(isDesktop, (desktop) => {
  if (desktop) drawerOpen.value = false;
});
</script>
<template>
  <!-- The shell owns the viewport: header, sidebar and the phone's bottom bar hold still and only the main region
       scrolls. The sidebar's width eases between full and rail unless the reader asks for reduced motion. -->
  <AppShell
    class="surface-ambient transition-[grid-template-columns] duration-200 ease-out motion-reduce:transition-none"
    sidebar-breakpoint="md"
    :sidebar-width="sidebarWidth"
  >
    <AppShellHeader
      height="auto"
      variant="glass"
      class="border-(--wh-glass-edge) p-0 pt-[env(safe-area-inset-top)] shadow-(--wh-glass-shadow)"
    >
      <div class="flex w-full min-w-0 items-stretch">
        <!-- From tablets up, the brand sits above the sidebar, as wide as it. -->
        <div
          class="hidden shrink-0 items-center gap-2.5 overflow-hidden border-r border-(--wh-glass-edge) transition-[width] duration-200 ease-out motion-reduce:transition-none md:flex"
          :class="isRail ? 'justify-center' : 'px-4'"
          :style="{ width: sidebarWidth }"
        >
          <RouterLink to="/" class="flex items-center gap-2.5" aria-label="Wheelhouse home">
            <BrandMark v-if="isRail" :size="28" />
            <BrandWordmark v-else :height="22" />
          </RouterLink>
          <span
            v-if="!isRail"
            class="rounded-md bg-muted px-1.5 py-0.5 font-mono text-[11px] font-medium text-muted-foreground"
            :title="`Console ${consoleVersion}`"
            >{{ versionLabel }}</span
          >
        </div>
        <div
          class="flex min-h-14 min-w-0 flex-1 flex-wrap items-center gap-x-3 gap-y-2 px-4 py-2 md:flex-nowrap md:py-0 lg:px-6"
        >
          <RouterLink to="/" class="shrink-0 md:hidden" aria-label="Wheelhouse home"><BrandMark :size="26" /></RouterLink>
          <Button
            variant="ghost"
            tone="neutral"
            size="sm"
            class="hidden shrink-0 md:inline-flex"
            :aria-label="isDesktop ? (collapsed ? 'Expand sidebar' : 'Collapse sidebar') : 'Open navigation'"
            :aria-expanded="isDesktop ? !collapsed : drawerOpen"
            :title="isDesktop ? (collapsed ? 'Expand sidebar' : 'Collapse sidebar') : 'Open navigation'"
            @click="toggleSidebar"
            ><PanelLeft :size="18"
          /></Button>
          <ScopeBar :crumbs="crumbs" />
          <Badge
            v-if="system.localRig.value"
            variant="warning"
            class="hidden shrink-0 sm:inline-flex"
            title="Deployments go to the local server"
            >Local server</Badge
          >
          <ProfileMenu
            class="shrink-0"
            :user="props.user"
            :connection="system.connection.value"
            :api-version="system.version.value"
            @sign-out="emit('signOut')"
          />
        </div>
      </div>
    </AppShellHeader>
    <AppShellSidebar class="border-(--wh-glass-edge) bg-transparent">
      <ConsoleNav :collapsed="isRail" />
    </AppShellSidebar>
    <AppShellMain ref="main">
      <!-- The widening gutter: 1rem on phones, 1.5rem on tablets and small desktops, 2rem from `xl`. -->
      <AppShellContent gutter="lg" max-width="2xl" class="lg:px-6 xl:px-8">
        <slot />
      </AppShellContent>
    </AppShellMain>
    <PhoneNav @sign-out="emit('signOut')" />
  </AppShell>
  <!-- Tablets keep the rail beside the content; the full sidebar opens over it. -->
  <NavDrawer v-if="breakpoint === 'md' && drawerWanted" v-model:open="drawerOpen" />
</template>
