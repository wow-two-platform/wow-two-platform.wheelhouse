<script lang="ts">
import type { ConsolePeer } from '@/domain/system';

/** Defines the environment tag that switches to the other console. */
export interface EnvironmentSwitchProps {
  /** The environment this console serves; the tag reads it. */
  readonly environment: string;
  /** The other console. */
  readonly peer: ConsolePeer;
  /** This page's path, query and hash, as the router names them. */
  readonly fullPath: string;
  /** The console build, shown in the menu and the tag's tooltip. */
  readonly version: string;
}
</script>
<script setup lang="ts">
import { ref } from 'vue';
import { ChevronDown } from 'lucide-vue-next';
import { Popover, PopoverContent, PopoverTrigger } from '@wow-two-beta/fe/presentation/overlays';
import EnvironmentSwitchMenu from './EnvironmentSwitchMenu.vue';

/**
 * The environment tag beside the logo, once the host names the other console (`Console:PeerUrl`): a small pill that
 * reads `dev` or `prod` and opens a menu with one link to the same page there. It opens on click only. On phones its
 * hit area grows to the 44px touch target without growing the pill.
 */
defineOptions({ name: 'EnvironmentSwitch' });
const props = defineProps<EnvironmentSwitchProps>();
defineSlots<{}>();
const open = ref(false);
</script>
<template>
  <Popover v-model:open="open" placement="bottom-start">
    <PopoverTrigger as-child>
      <button
        type="button"
        class="relative inline-flex shrink-0 items-center gap-0.5 rounded-md bg-muted px-1.5 py-0.5 font-mono text-[11px] font-medium text-muted-foreground transition-colors before:absolute before:-inset-3 before:content-[''] hover:text-foreground focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-ring aria-expanded:text-foreground md:before:hidden"
        :aria-label="`${props.environment} console, switch to ${props.peer.environment}`"
        :title="`Console ${props.version}`"
        data-environment-tag
      >
        {{ props.environment }}
        <ChevronDown :size="10" aria-hidden="true" />
      </button>
    </PopoverTrigger>
    <PopoverContent aria-label="Switch console" class="w-[min(20rem,calc(100vw-2rem))] p-1.5">
      <EnvironmentSwitchMenu
        :environment="props.environment"
        :peer="props.peer"
        :full-path="props.fullPath"
        :version="props.version"
        @open="open = false"
      />
    </PopoverContent>
  </Popover>
</template>
