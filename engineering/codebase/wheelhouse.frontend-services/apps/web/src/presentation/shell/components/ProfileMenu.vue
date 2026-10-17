<script lang="ts">
import type { CurrentUser } from '@/domain/auth';

/** Defines account controls for the top navigation bar. */
export interface ProfileMenuProps {
  readonly user: CurrentUser;
  readonly connection: string;
  /** The API's informational version, once the status read answers. */
  readonly apiVersion?: string | null;
}
</script>
<script setup lang="ts">
import { computed, defineAsyncComponent, ref } from 'vue';
import { useRouter } from 'vue-router';
import { Check, ChevronDown, LogOut } from 'lucide-vue-next';
import { TailwindBreakpoints, useBreakpoint } from '@wow-two-beta/fe/foundation/device';
import { useColorMode } from '@wow-two-beta/fe/foundation/primitives';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { Avatar } from '@wow-two-beta/fe/presentation/display';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuTrigger,
  MenuCheckboxItem,
  MenuItem,
  MenuLabel,
  MenuRadioGroup,
  MenuRadioItem,
  MenuSeparator,
} from '@wow-two-beta/fe/presentation/nav';
import { useShowInactive } from '@/application/preferences';
import { AppearanceSchemes, ProfileLinkGroups, shortVersion } from './ProfileLinks';

/** The phone's sheet loads the first time it opens; desktops and tablets never need it. @internal */
const ProfileSheet = defineAsyncComponent(() => import('./ProfileSheet.vue'));

/**
 * The avatar in the top bar opens everything the primary navigation leaves out: the portfolio-wide Activity and
 * Secrets, settings, "Show inactive", appearance and sign-out — a menu from tablets up, a bottom sheet on phones. The
 * trigger is the SDK's neutral outline button at the top bar's size, so it matches the buttons beside it.
 */
defineOptions({ name: 'ProfileMenu' });
const props = defineProps<ProfileMenuProps>();
const emit = defineEmits<{ signOut: [] }>();
defineSlots<{}>();
const router = useRouter();
const colorMode = useColorMode();
const showInactive = useShowInactive();
const breakpoint = useBreakpoint(TailwindBreakpoints);
const sheetOpen = ref(false);
/** True from the first time the sheet opens; until then its code is not loaded. */
const sheetWanted = ref(false);
/** Below `md`: the profile opens as a bottom sheet. */
const isPhone = computed(() => !['md', 'lg', 'xl', '2xl'].includes(breakpoint.value ?? ''));
/** The appearance the operator chose: `system` while the mode follows the operating system. */
const appearance = computed(() => (colorMode.followsSystem ? 'system' : colorMode.mode));
const consoleVersion = __APP_VERSION__;
const displayName = computed(() => props.user.name || props.user.login);

/** Applies an appearance row; the SDK provider persists the choice. @internal */
function choose(value: string | null): void {
  if (value === 'system') colorMode.followSystem();
  else if (value === 'light' || value === 'dark') colorMode.setMode(value);
}

/** Opens the phone's sheet, loading it the first time. @internal */
function openSheet(): void {
  sheetWanted.value = true;
  sheetOpen.value = true;
}
</script>
<template>
  <!-- One root, so the top bar's layout classes land on it rather than on whichever trigger shows. -->
  <div class="flex shrink-0 items-center">
  <Button
    v-if="isPhone"
    variant="outline"
    tone="neutral"
    size="md"
    class="px-1"
    aria-haspopup="dialog"
    :aria-expanded="sheetOpen"
    :aria-label="`Account menu for ${displayName}`"
    @click="openSheet"
  >
    <Avatar v-bind="props.user.avatar ? { src: props.user.avatar } : {}" :name="displayName" size="sm" />
  </Button>
  <DropdownMenu v-else placement="bottom-end">
    <DropdownMenuTrigger as-child>
      <Button
        variant="outline"
        tone="neutral"
        size="md"
        class="gap-1.5 ps-1 pe-2"
        :aria-label="`Account menu for ${displayName}`"
      >
        <Avatar v-bind="props.user.avatar ? { src: props.user.avatar } : {}" :name="displayName" size="sm" />
        <ChevronDown :size="14" class="text-muted-foreground" />
      </Button>
    </DropdownMenuTrigger>
    <DropdownMenuContent aria-label="Account" class="w-64">
      <MenuLabel>{{ displayName }}</MenuLabel>
      <p class="px-2 pb-1 text-xs text-muted-foreground">@{{ props.user.login }} · API {{ props.connection }}</p>
      <p class="px-2 pb-2 font-mono text-[11px] text-muted-foreground">
        Console {{ consoleVersion }} · API {{ props.apiVersion ? shortVersion(props.apiVersion) : '—' }}
      </p>
      <template v-for="group in ProfileLinkGroups" :key="group.label">
        <MenuSeparator />
        <MenuLabel>{{ group.label }}</MenuLabel>
        <MenuItem v-for="link in group.links" :key="link.path" @select="router.push(link.path)"
          ><component :is="link.icon" :size="15" />{{ link.label }}</MenuItem
        >
      </template>
      <MenuSeparator />
      <MenuCheckboxItem v-model="showInactive" :can-close-on-select="false">
        <template #indicator="{ checked }"><Check v-if="checked" :size="14" /></template>
        Show inactive
      </MenuCheckboxItem>
      <MenuRadioGroup label="Appearance" :model-value="appearance" @update:model-value="choose">
        <MenuRadioItem v-for="item in AppearanceSchemes" :key="item.value" :value="item.value">
          <template #indicator="{ checked }"><Check v-if="checked" :size="14" /></template>
          <component :is="item.icon" :size="15" />{{ item.label }}
        </MenuRadioItem>
      </MenuRadioGroup>
      <MenuSeparator />
      <MenuItem state="destructive" @select="emit('signOut')"><LogOut :size="15" />Sign out</MenuItem>
    </DropdownMenuContent>
  </DropdownMenu>
  <ProfileSheet
    v-if="sheetWanted"
    v-model:open="sheetOpen"
    :user="props.user"
    :connection="props.connection"
    :api-version="props.apiVersion ?? null"
    @sign-out="emit('signOut')"
  />
  </div>
</template>
