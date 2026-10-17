<script setup lang="ts">
import { computed } from "vue";
import { RouterLink, useRoute } from "vue-router";
import { CheckCircle2 } from "lucide-vue-next";
import { Text } from "@wow-two-beta/fe/presentation/display";
import { DataRegion, useSkeletonItems } from "@wow-two-beta/fe/presentation/feedback";
import { usePortfolioAttention } from "@/application/overview";

/** Lists what needs the operator across every product: unreachable hosts, failing containers, rotation debt. */
defineOptions({ name: "PortfolioAttention" });
defineSlots<{}>();

const route = useRoute();
const { items: attention, incomplete, loading } = usePortfolioAttention();
/** The first read: nothing to list yet while any source still loads. */
const firstLoad = computed(() => loading.value && !attention.value.length);
/** The items, or one placeholder row during the first read. */
const items = useSkeletonItems(() => (firstLoad.value ? undefined : attention.value), 1);
</script>
<template>
  <section
    class="rounded-2xl border border-border bg-card p-5"
    aria-label="Portfolio attention"
  >
    <div class="mb-3 flex items-center justify-between">
      <h2 class="text-sm font-semibold">Needs attention</h2>
      <span class="text-xs text-muted-foreground">All products</span>
    </div>
    <DataRegion :is-first-load="firstLoad" loading-label="Reading portfolio attention">
      <div
        v-for="(item, index) in items"
        :key="item?.id ?? `placeholder-${index}`"
        class="flex items-start justify-between gap-3 border-t border-border py-3"
      >
        <div class="min-w-0 flex-1">
          <Text
            class="text-sm font-medium"
            :class="item?.tone === 'danger' ? 'text-destructive' : 'text-warning'"
            skeleton-width="50%"
          >
            {{ item?.title }}
          </Text>
          <Text class="mt-1 text-xs text-muted-foreground" skeleton-width="80%">{{ item?.detail }}</Text>
        </div>
        <RouterLink
          v-if="item"
          :to="{ path: item.href, query: route.query }"
          class="shrink-0 text-xs font-medium text-primary"
          >Open</RouterLink
        >
      </div>
      <p
        v-if="!attention.length && !loading && !incomplete"
        class="flex items-center gap-2 text-sm text-muted-foreground"
      >
        <CheckCircle2 :size="16" class="text-success" />No issues in the available
        readings.
      </p>
      <p v-if="incomplete" role="status" class="text-xs text-warning">
        Some readings are unavailable; this list may be incomplete.
      </p>
    </DataRegion>
  </section>
</template>
