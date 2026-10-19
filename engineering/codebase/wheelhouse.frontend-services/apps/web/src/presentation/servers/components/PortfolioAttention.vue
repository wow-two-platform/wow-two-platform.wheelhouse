<script setup lang="ts">
import { RouterLink, useRoute } from "vue-router";
import { usePortfolioAttention } from "@/application/overview";

/**
 * Lists what needs the owner across every product: unreachable hosts, failing containers, rotation debt. Rendered
 * only while something does — a healthy portfolio, and a reading still on its way, leave no empty panel.
 */
defineOptions({ name: "PortfolioAttention" });
defineSlots<{}>();

const route = useRoute();
const { items, incomplete } = usePortfolioAttention();
</script>
<template>
  <section
    v-if="items.length"
    class="rounded-2xl border border-border bg-card p-5"
    aria-label="Portfolio attention"
  >
    <div class="mb-3 flex items-center justify-between">
      <h2 class="text-sm font-semibold">Needs attention</h2>
      <span class="text-xs text-muted-foreground">All products</span>
    </div>
    <div
      v-for="item in items"
      :key="item.id"
      class="flex items-start justify-between gap-3 border-t border-border py-3"
    >
      <div class="min-w-0 flex-1">
        <p class="text-sm font-medium" :class="item.tone === 'danger' ? 'text-destructive' : 'text-warning'">
          {{ item.title }}
        </p>
        <p class="mt-1 text-xs text-muted-foreground">{{ item.detail }}</p>
      </div>
      <RouterLink
        :to="{ path: item.href, query: route.query }"
        class="shrink-0 text-xs font-medium text-primary"
        >Open</RouterLink
      >
    </div>
    <p v-if="incomplete" role="status" class="text-xs text-warning">
      Some readings are unavailable; this list may be incomplete.
    </p>
  </section>
</template>
