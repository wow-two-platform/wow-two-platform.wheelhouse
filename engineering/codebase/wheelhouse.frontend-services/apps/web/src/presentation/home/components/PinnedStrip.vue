<script setup lang="ts">
import { defineAsyncComponent, ref } from 'vue';
import { RouterLink } from 'vue-router';
import { Check, Pencil } from 'lucide-vue-next';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { DataRegion, SkeletonState } from '@wow-two-beta/fe/presentation/feedback';
import { usePinned } from '@/application/pins';
import { PinIcon } from '@/presentation/pins';

/**
 * The pins on the home page, in the operator's order: a row that scrolls sideways, with Edit for reordering and
 * unpinning. On phones it leads the page, since the sidebar that also lists pins is absent there.
 */
/** The editor loads when editing starts; it draws on the SDK's sortable list, which browsing never needs. @internal */
const PinEditor = defineAsyncComponent(() => import('@/presentation/pins/PinEditor.vue'));

defineOptions({ name: 'PinnedStrip' });
defineSlots<{}>();
const pinned = usePinned();
const editing = ref(false);
</script>
<template>
  <section
    v-if="pinned.isFirstLoad.value || pinned.entries.value.length"
    aria-labelledby="pinned-heading"
    class="min-w-0"
  >
    <header class="mb-2 flex items-center justify-between gap-3">
      <h2 id="pinned-heading" class="text-sm font-semibold text-muted-foreground">Pinned</h2>
      <Button
        v-if="pinned.entries.value.length"
        size="sm"
        variant="ghost"
        tone="neutral"
        :aria-pressed="editing"
        @click="editing = !editing"
      >
        <template #leading><Check v-if="editing" :size="14" /><Pencil v-else :size="14" /></template
        >{{ editing ? 'Done' : 'Edit' }}
      </Button>
    </header>
    <DataRegion :is-first-load="pinned.isFirstLoad.value" loading-label="Loading pins">
      <template #skeleton>
        <div class="flex gap-2 overflow-hidden" aria-hidden="true">
          <SkeletonState v-for="index in 3" :key="index" class="h-12 w-40 shrink-0 rounded-xl" />
        </div>
      </template>
      <div v-if="editing" class="max-w-xl rounded-2xl border border-border bg-card p-2">
        <PinEditor :entries="pinned.entries.value" />
      </div>
      <!-- Bleeds to the screen edges on phones, so the row reads as one that scrolls. -->
      <ul
        v-else
        role="list"
        class="-mx-4 flex snap-x snap-mandatory gap-2 overflow-x-auto px-4 pb-1 [scrollbar-width:none] sm:-mx-6 sm:px-6 md:mx-0 md:flex-wrap md:overflow-visible md:px-0"
      >
        <li v-for="entry in pinned.visible.value" :key="entry.pin.id" class="shrink-0 snap-start">
          <RouterLink
            :to="entry.location!"
            class="flex min-h-12 max-w-64 items-center gap-2.5 rounded-xl border border-border bg-card px-3 py-2 text-sm transition-colors hover:border-primary/40 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
            :class="entry.active ? '' : 'opacity-60'"
          >
            <PinIcon :entry="entry" size="sm" />
            <span class="min-w-0">
              <span class="block truncate font-medium">{{ entry.label }}</span>
              <span v-if="entry.detail" class="block truncate text-xs text-muted-foreground">{{ entry.detail }}</span>
            </span>
          </RouterLink>
        </li>
      </ul>
    </DataRegion>
  </section>
</template>
