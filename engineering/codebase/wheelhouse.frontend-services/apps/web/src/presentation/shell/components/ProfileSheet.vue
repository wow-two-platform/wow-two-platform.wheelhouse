<script lang="ts">
import type { CurrentUser } from '@/domain/auth';

/** The phone's profile sheet: the account, the places the bottom bar leaves out, and the console's view choices. */
export interface ProfileSheetProps {
  /** The open state — the `v-model:open` target. */
  readonly open: boolean;
  readonly user: CurrentUser;
  readonly connection: string;
  readonly apiVersion: string | null;
}
</script>
<script setup lang="ts">
import { computed, watch } from 'vue';
import { RouterLink, useRoute } from 'vue-router';
import { LogOut } from 'lucide-vue-next';
import { useColorMode } from '@wow-two-beta/fe/foundation/primitives';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { Avatar } from '@wow-two-beta/fe/presentation/display';
import { ToggleGroup, ToggleInput } from '@wow-two-beta/fe/presentation/forms';
import { SidebarMenu, SidebarMenuItem, SidebarMenuSection } from '@wow-two-beta/fe/presentation/nav';
import { BottomSheet, BottomSheetTitle } from '@wow-two-beta/fe/presentation/overlays';
import { AppearanceSchemes, ProfileLinkGroups, shortVersion } from './ProfileLinks';
import ShowInactiveSwitch from './ShowInactiveSwitch.vue';

/**
 * What the profile menu holds, as a bottom sheet for phones: Activity, Secrets, the settings pages, "Show inactive",
 * appearance and sign-out. Loaded the first time it opens; any navigation closes it.
 */
defineOptions({ name: 'ProfileSheet' });
const props = defineProps<ProfileSheetProps>();
const emit = defineEmits<{ 'update:open': [open: boolean]; signOut: [] }>();
defineSlots<{}>();
const route = useRoute();
const colorMode = useColorMode();
const consoleVersion = __APP_VERSION__;
const displayName = computed(() => props.user.name || props.user.login);
/** The appearance the operator chose: `system` while the mode follows the operating system. */
const appearance = computed(() => (colorMode.followsSystem ? 'system' : colorMode.mode));

/** Whether a place is the current page, including the pages below it. @internal */
function isAt(path: string): boolean {
  return route.path === path || route.path.startsWith(`${path}/`);
}

/** Applies an appearance segment; pressing the active one keeps it. @internal */
function choose(value: unknown): void {
  if (value === 'system') colorMode.followSystem();
  else if (value === 'light' || value === 'dark') colorMode.setMode(value);
}

/* Any navigation closes the sheet. */
watch(
  () => route.fullPath,
  () => emit('update:open', false),
);
</script>
<template>
  <BottomSheet :open="props.open" :snap-points="[600, '92vh']" @update:open="emit('update:open', $event)">
    <BottomSheetTitle class="sr-only">Account</BottomSheetTitle>
    <div class="flex items-center gap-3 px-1 pb-3">
      <Avatar v-bind="props.user.avatar ? { src: props.user.avatar } : {}" :name="displayName" size="md" />
      <div class="min-w-0 flex-1">
        <p class="truncate text-sm font-semibold">{{ displayName }}</p>
        <p class="truncate text-xs text-muted-foreground">@{{ props.user.login }} · API {{ props.connection }}</p>
        <p class="truncate font-mono text-[11px] text-muted-foreground">
          Console {{ consoleVersion }} · API {{ props.apiVersion ? shortVersion(props.apiVersion) : '—' }}
        </p>
      </div>
    </div>
    <SidebarMenu aria-label="Account" class="border-t border-border pt-1">
      <SidebarMenuSection v-for="group in ProfileLinkGroups" :key="group.label" :label="group.label">
        <SidebarMenuItem
          v-for="link in group.links"
          :key="link.path"
          as-child
          class="h-11"
          :is-active="isAt(link.path)"
        >
          <template #icon><component :is="link.icon" :size="18" /></template>
          <RouterLink :to="link.path">{{ link.label }}</RouterLink>
        </SidebarMenuItem>
      </SidebarMenuSection>
    </SidebarMenu>
    <div class="mt-2 flex flex-col gap-2 border-t border-border pt-2">
      <ShowInactiveSwitch class="min-h-11" />
      <div class="flex min-h-11 items-center justify-between gap-3 px-2.5">
        <span id="profile-appearance" class="text-sm text-muted-foreground">Appearance</span>
        <ToggleGroup
          variant="segmented"
          aria-labelledby="profile-appearance"
          :model-value="appearance"
          @update:model-value="choose"
        >
          <ToggleInput v-for="item in AppearanceSchemes" :key="item.value" :value="item.value" size="sm" :title="item.label"
            ><component :is="item.icon" :size="14" /><span class="sr-only">{{ item.label }}</span></ToggleInput
          >
        </ToggleGroup>
      </div>
      <Button variant="ghost" tone="danger" is-full-width class="h-11 justify-start" @click="emit('signOut')">
        <template #leading><LogOut :size="18" /></template>Sign out
      </Button>
    </div>
  </BottomSheet>
</template>
