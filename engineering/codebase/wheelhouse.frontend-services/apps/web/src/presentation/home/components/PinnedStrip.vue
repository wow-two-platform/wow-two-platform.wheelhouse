<script setup lang="ts">
import { defineAsyncComponent, ref } from 'vue';
import { RouterLink } from 'vue-router';
import { Check, Pencil } from 'lucide-vue-next';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { DataRegion, SkeletonState } from '@wow-two-beta/fe/presentation/feedback';
import { usePinned } from '@/application/pins';
import { PinIcon } from '@/presentation/pins';

/**
 * The pinned products on a phone's home page, in the owner's order: one compact row of chips that scrolls sideways,
 * with Edit for reordering and unpinning. Phones have no sidebar, which holds the pins on larger screens; the page
 * shows this strip below `md` only.
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
    <header class="mb-1.5 flex items-center justify-between gap-3">
      <h2 id="pinned-heading" class="text-xs font-semibold text-muted-foreground">Pinned</h2>
      <Button
        v-if="pinned.entries.value.length"
        size="sm"
        variant="ghost"
        tone="neutral"
        class="max-md:min-h-11"
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
          <SkeletonState v-for="index in 3" :key="index" class="h-10 w-32 shrink-0 rounded-full" />
        </div>
      </template>
      <div v-if="editing" class="rounded-2xl border border-border bg-card p-2">
        <PinEditor :entries="pinned.entries.value" />
      </div>
      <!-- Bleeds to the screen edges, so the row reads as one that scrolls. -->
      <ul
        v-else
        role="list"
        class="-mx-4 flex snap-x snap-mandatory gap-2 overflow-x-auto px-4 pb-1 [scrollbar-width:none]"
      >
        <li v-for="entry in pinned.visible.value" :key="entry.pin.id" class="shrink-0 snap-start">
          <RouterLink
            :to="entry.location!"
            class="flex min-h-11 max-w-52 items-center gap-2 rounded-full border border-border bg-card ps-1.5 pe-3.5 text-[13px] transition-colors hover:border-primary/40 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
            :class="entry.active ? '' : 'opacity-60'"
          >
            <PinIcon :entry="entry" />
            <span class="min-w-0 truncate font-medium">{{ entry.label }}</span>
          </RouterLink>
        </li>
      </ul>
    </DataRegion>
  </section>
</template>
