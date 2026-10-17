<script lang="ts">
/** The sidebar's Pinned section. */
export interface PinnedNavProps {
  readonly collapsed?: boolean;
}
</script>
<script setup lang="ts">
import { computed, ref } from 'vue';
import { RouterLink, useRoute } from 'vue-router';
import { Check, Pencil } from 'lucide-vue-next';
import { SkeletonState } from '@wow-two-beta/fe/presentation/feedback';
import { SidebarMenuItem, SidebarMenuSection } from '@wow-two-beta/fe/presentation/nav';
import { usePinned, type PinnedEntry } from '@/application/pins';
import { PinEditor, PinIcon } from '@/presentation/pins';

/**
 * Pinned workspaces, products and services in the operator's order. Edit turns the rows into a reorder list; the rail
 * shows the pins as icons and leaves editing to the full sidebar.
 */
defineOptions({ name: 'PinnedNav' });
const props = withDefaults(defineProps<PinnedNavProps>(), { collapsed: false });
const emit = defineEmits<{ navigate: [] }>();
defineSlots<{}>();
const route = useRoute();
const pinned = usePinned();
const editing = ref(false);
const isEditing = computed(() => editing.value && !props.collapsed);

/** Whether the pin's target is the current page. @internal */
function isCurrent(entry: PinnedEntry): boolean {
  if (entry.workspaceSlug) return route.params['workspace'] === entry.workspaceSlug;
  if (route.params['product'] !== entry.productSlug) return false;
  const inspected = route.query['inspect'] === 'service' ? route.query['item'] : null;
  return entry.service ? inspected === entry.service : inspected == null;
}
</script>
<template>
  <SidebarMenuSection v-if="pinned.isFirstLoad.value || pinned.entries.value.length" label="Pinned" class="relative">
    <!-- The SDK section has no action slot; the Edit control sits on its heading row. -->
    <li v-if="!props.collapsed && pinned.entries.value.length" class="absolute end-0 top-2.5">
      <button
        type="button"
        class="inline-flex h-6 items-center gap-1 rounded-md px-1.5 text-xs font-medium text-muted-foreground hover:bg-muted hover:text-foreground focus-visible:outline-2 focus-visible:outline-ring"
        :aria-pressed="isEditing"
        @click="editing = !editing"
      >
        <Check v-if="isEditing" :size="12" /><Pencil v-else :size="12" />{{ isEditing ? 'Done' : 'Edit' }}
      </button>
    </li>
    <li v-if="pinned.isFirstLoad.value" aria-hidden="true" class="flex flex-col gap-1.5 py-1">
      <SkeletonState v-for="index in 2" :key="index" class="h-8 w-full rounded-md" />
    </li>
    <li v-else-if="isEditing"><PinEditor :entries="pinned.entries.value" compact /></li>
    <template v-else>
      <SidebarMenuItem
        v-for="entry in pinned.visible.value"
        :key="entry.pin.id"
        as-child
        :is-active="isCurrent(entry)"
      >
        <template #icon><PinIcon :entry="entry" /></template>
        <RouterLink
          :to="entry.location!"
          :title="props.collapsed ? (entry.detail ? `${entry.label} · ${entry.detail}` : entry.label) : undefined"
          :class="entry.active ? '' : 'opacity-60'"
          @click="emit('navigate')"
          ><span :class="props.collapsed ? 'sr-only' : 'min-w-0 flex-1 truncate text-start'"
            >{{ entry.label
            }}<span v-if="entry.detail" class="text-xs font-normal text-muted-foreground"> · {{ entry.detail }}</span></span
          ></RouterLink
        >
      </SidebarMenuItem>
    </template>
  </SidebarMenuSection>
</template>
