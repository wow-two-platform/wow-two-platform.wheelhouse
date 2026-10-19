<script lang="ts">
import type { ConsolePeer } from '@/domain/system';

/** Defines what the environment tag's menu offers. */
export interface EnvironmentSwitchMenuProps {
  /** The environment this console serves. */
  readonly environment: string;
  /** The other console. */
  readonly peer: ConsolePeer;
  /** This page's path, query and hash, as the router names them. */
  readonly fullPath: string;
  /** The console build, which the tag no longer shows. */
  readonly version: string;
}
</script>
<script setup lang="ts">
import { computed } from 'vue';
import { ArrowUpRight } from 'lucide-vue-next';
import { ConsolePeerRules } from '@/domain/system';

/**
 * The menu behind the environment tag: one link that opens this page on the other console — the same path, query and
 * hash — with the page it will open and a one-line hint. A plain link, so the browser's own gestures work: Enter or a
 * tap opens it here, and Cmd or Ctrl click opens it in a new tab.
 */
defineOptions({ name: 'EnvironmentSwitchMenu' });
const props = defineProps<EnvironmentSwitchMenuProps>();
const emit = defineEmits<{ open: [] }>();
defineSlots<{}>();
const href = computed(() => ConsolePeerRules.hrefOf(props.peer.url, props.fullPath));
</script>
<template>
  <div class="flex min-w-0 flex-col">
    <p class="px-2 pt-0.5 pb-1 font-mono text-[11px] text-muted-foreground">
      {{ props.environment }} console · {{ props.version }}
    </p>
    <a
      :href="href"
      rel="noopener"
      data-console-switch
      class="flex min-w-0 items-center gap-2 rounded-md px-2 py-2 transition-colors hover:bg-muted focus-visible:outline-2 focus-visible:outline-ring max-md:min-h-11"
      @click="emit('open')"
    >
      <span class="flex min-w-0 flex-1 flex-col">
        <span class="text-sm font-medium text-foreground">Open {{ props.peer.environment }}</span>
        <span class="truncate font-mono text-xs text-muted-foreground" :title="props.fullPath">{{ props.fullPath }}</span>
      </span>
      <ArrowUpRight :size="16" class="shrink-0 text-muted-foreground" aria-hidden="true" />
    </a>
    <p class="px-2 pt-1 pb-0.5 text-xs text-muted-foreground">
      Same page and filters, on the {{ props.peer.environment }} console.
    </p>
  </div>
</template>
