<script lang="ts">
import type { CurrentUser } from '@/domain/auth';

/** Defines the authenticated workspace frame. */
export interface AppLayoutProps {
  readonly user: CurrentUser;
}
</script>
<script setup lang="ts">
import { useTemplateRef, watch } from 'vue';
import { RouterLink, useRoute } from 'vue-router';
import { Badge } from '@wow-two-beta/fe/presentation/display';
import { AppShell, AppShellContent, AppShellHeader, AppShellMain } from '@wow-two-beta/fe/presentation/layout';
import { AppNavLink, RoutePageHeader } from '@wow-two-beta/fe/router';
import { useApiConnection } from '@/application/system';
import { BrandWordmark, ProfileMenu } from '@/presentation/shell';
import { AppPlaces } from './routes';

/** Combines the deployment desk's top navigation with a contextual studio workspace. */
defineOptions({ name: 'AppLayout' });
const props = defineProps<AppLayoutProps>();
const emit = defineEmits<{ signOut: [] }>();
defineSlots<{ default(): unknown }>();
const route = useRoute();
const system = useApiConnection();
const consoleVersion = __APP_VERSION__;
/** A numbered build shows as `v0.3.4`; a local build shows `dev`. @internal */
const versionLabel = /^\d/.test(consoleVersion) ? `v${consoleVersion}` : consoleVersion;
const main = useTemplateRef<{ el: HTMLElement | null }>('main');

/* The main region is the scroll container, so a new page starts at its top. A selection inside a page (a query
   change) keeps its place; the list-detail pages manage their own scroll as their panes swap. */
watch(
  () => route.path,
  () => main.value?.el?.scrollTo({ top: 0 }),
);
</script>
<template>
  <!-- The shell owns the viewport: the header spans the full window and only the main region scrolls, so no
       platform scrollbar ever cuts the header short. -->
  <AppShell class="surface-ambient">
    <!-- The SDK glass bar keeps the console's glass edge and depth. The shell's column floors at zero, so the
         navigation's full width never widens it past a phone screen; the navigation scrolls instead. -->
    <AppShellHeader
      height="auto"
      variant="glass"
      class="border-(--wh-glass-edge) p-0 shadow-(--wh-glass-shadow)"
    >
      <!-- One row on wider screens; on phones the navigation wraps to its own row and scrolls sideways. Its edges
           follow the content's gutter and 1536px measure. -->
      <div class="mx-auto flex w-full max-w-[1600px] flex-wrap items-center gap-x-7 px-4 sm:px-6 xl:px-8">
        <RouterLink
          :to="{ path: '/', query: route.query }"
          class="flex h-[72px] shrink-0 items-center gap-2.5"
          aria-label="Wheelhouse workspace"
        >
          <BrandWordmark :height="26" />
          <span
            class="rounded-md bg-muted px-1.5 py-0.5 font-mono text-[11px] font-medium text-muted-foreground"
            :title="`Console ${consoleVersion}`"
            >{{ versionLabel }}</span
          >
        </RouterLink>
        <nav
          aria-label="Primary navigation"
          class="order-3 -mx-4 flex w-[calc(100%+2rem)] shrink-0 gap-1 overflow-x-auto px-4 pb-2 sm:-mx-6 sm:w-[calc(100%+3rem)] sm:px-6 md:order-none md:mx-0 md:w-auto md:shrink md:flex-1 md:p-0"
        >
          <AppNavLink
            v-for="place in AppPlaces"
            :key="place.path"
            :to="{ path: place.path, query: route.query }"
            is-exact
            class="w-auto shrink-0 whitespace-nowrap rounded-lg px-3 text-muted-foreground hover:text-foreground"
            >{{ place.label }}</AppNavLink
          >
        </nav>
        <div class="ml-auto flex items-center gap-3">
          <Badge v-if="system.localRig.value" variant="warning" title="Deployments go to the local server"
            >Local server</Badge
          >
          <ProfileMenu
            :user="props.user"
            :connection="system.connection.value"
            :api-version="system.version.value"
            @sign-out="emit('signOut')"
          />
        </div>
      </div>
    </AppShellHeader>
    <AppShellMain ref="main">
      <!-- The widening gutter, held at 1.5rem until `xl` as the console always has. -->
      <AppShellContent gutter="lg" max-width="2xl" class="lg:px-6 xl:px-8">
        <!-- The route handle's title and description; a page's PageHeaderActions land beside them. -->
        <RoutePageHeader class="mb-6" />
        <slot />
      </AppShellContent>
    </AppShellMain>
  </AppShell>
</template>
