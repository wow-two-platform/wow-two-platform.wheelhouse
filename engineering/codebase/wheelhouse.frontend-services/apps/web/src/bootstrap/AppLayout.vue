<script lang="ts">
import type { CurrentUser } from '@/domain/auth';

/** Defines the authenticated workspace frame. */
export interface AppLayoutProps {
  readonly user: CurrentUser;
}
</script>
<script setup lang="ts">
import { computed, useTemplateRef, watch } from 'vue';
import { RouterLink, useRoute } from 'vue-router';
import { Badge } from '@wow-two-beta/ui-vue/presentation/display';
import { useApiConnection } from '@/application/system';
import { BrandWordmark, ProfileMenu } from '@/presentation/shell';
import { AppPlaces, SettingsPlaces } from './routes';

/** Combines the deployment desk's top navigation with a contextual studio workspace. */
defineOptions({ name: 'AppLayout' });
const props = defineProps<AppLayoutProps>();
const emit = defineEmits<{ signOut: [] }>();
defineSlots<{ default(): unknown }>();
const route = useRoute();
const system = useApiConnection();
const current = computed(() => [...AppPlaces, ...SettingsPlaces].find((place) => place.path === route.path));
const consoleVersion = __APP_VERSION__;
/** A numbered build shows as `v0.3.4`; a local build shows `dev`. @internal */
const versionLabel = /^\d/.test(consoleVersion) ? `v${consoleVersion}` : consoleVersion;
const mainElement = useTemplateRef<HTMLElement>('main');

/** The main region is the scroll container, so a new page starts at its top. */
watch(
  () => route.path,
  () => mainElement.value?.scrollTo({ top: 0 }),
);
</script>
<template>
  <!-- The frame owns the viewport: the header spans the full window and only the main region scrolls, so no
       platform scrollbar ever cuts the header short. -->
  <div class="wh-ambient flex h-dvh flex-col overflow-hidden text-foreground">
    <a
      href="#main-content"
      class="sr-only focus:not-sr-only focus:fixed focus:left-4 focus:top-4 focus:z-50 focus:rounded-lg focus:bg-card focus:p-3"
      >Skip to content</a
    >
    <header class="wh-glass wh-glass-navigation relative z-30 shrink-0 border-b">
      <div class="mx-auto flex max-w-[1600px] flex-wrap items-center gap-x-7 px-4 sm:px-6 xl:px-8">
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
          class="order-3 -mx-4 flex w-[calc(100%+2rem)] shrink-0 gap-1 overflow-x-auto px-4 pb-2 md:order-none md:mx-0 md:w-auto md:shrink md:flex-1 md:p-0"
        >
          <RouterLink
            v-for="place in AppPlaces"
            :key="place.path"
            :to="{ path: place.path, query: route.query }"
            :aria-current="route.path === place.path ? 'page' : undefined"
            class="whitespace-nowrap rounded-lg px-3 py-2 text-sm font-medium transition-colors focus-visible:outline-2 focus-visible:outline-ring"
            :class="
              route.path === place.path
                ? 'bg-primary-soft text-primary-soft-foreground'
                : 'text-muted-foreground hover:bg-muted hover:text-foreground'
            "
            >{{ place.label }}</RouterLink
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
    </header>
    <main
      id="main-content"
      ref="main"
      tabindex="-1"
      class="min-h-0 flex-1 overflow-y-auto [scrollbar-gutter:stable] focus:outline-none"
    >
      <div class="mx-auto max-w-[1600px] px-4 py-6 sm:px-6 lg:py-8 xl:px-8">
        <header class="mb-6 flex flex-wrap items-end justify-between gap-4">
          <div>
            <h1 class="text-2xl font-semibold tracking-tight">{{ current?.label }}</h1>
            <p class="mt-1 text-sm text-muted-foreground">{{ current?.description }}</p>
          </div>
          <div id="page-actions" class="flex flex-wrap items-center gap-2 empty:hidden" />
        </header>
        <slot />
      </div>
    </main>
  </div>
</template>
