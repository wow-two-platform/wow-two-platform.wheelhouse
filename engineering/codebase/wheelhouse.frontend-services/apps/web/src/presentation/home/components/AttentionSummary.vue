<script setup lang="ts">
import { computed } from 'vue';
import { RouterLink } from 'vue-router';
import { AlertTriangle } from 'lucide-vue-next';
import { usePortfolioAttention } from '@/application/overview';

/** @internal How many items the home page lists before linking to the rest. */
const Shown = 3;

/**
 * What needs the owner, at the top of the home page — only while something does. Healthy portfolios stay quiet,
 * and a still-loading reading never flashes an empty box.
 */
defineOptions({ name: 'AttentionSummary' });
defineSlots<{}>();
const attention = usePortfolioAttention();
const items = computed(() => attention.items.value.slice(0, Shown));
const more = computed(() => attention.items.value.length - items.value.length);
</script>
<template>
  <section
    v-if="attention.items.value.length"
    aria-labelledby="attention-heading"
    class="rounded-2xl border border-warning/30 bg-warning-soft/60 p-4 sm:p-5"
  >
    <div class="mb-2 flex items-center justify-between gap-3">
      <h2 id="attention-heading" class="flex items-center gap-2 text-sm font-semibold">
        <AlertTriangle :size="16" class="text-warning" />Needs attention
        <span class="tabular-nums text-muted-foreground">{{ attention.items.value.length }}</span>
      </h2>
      <RouterLink to="/servers" class="text-xs font-medium text-primary hover:underline">View all</RouterLink>
    </div>
    <ul role="list" class="divide-y divide-border/60">
      <li v-for="item in items" :key="item.id" class="flex items-start justify-between gap-3 py-2">
        <div class="min-w-0">
          <p class="text-sm font-medium" :class="item.tone === 'danger' ? 'text-destructive' : 'text-warning-soft-foreground'">
            {{ item.title }}
          </p>
          <p class="mt-0.5 line-clamp-2 text-xs text-muted-foreground">{{ item.detail }}</p>
        </div>
        <RouterLink :to="item.href" class="shrink-0 text-xs font-medium text-primary hover:underline">Open</RouterLink>
      </li>
    </ul>
    <p v-if="more > 0" class="mt-1 text-xs text-muted-foreground">And {{ more }} more.</p>
  </section>
</template>
