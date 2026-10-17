<script lang="ts">
import type { DeploymentFilterState, EntityTree } from "@/domain/deployments";

/** Defines the page-level filter strip of the Deployments page. */
export interface DeploymentFilterScopeProps {
  /** The filter in force. */
  readonly filter: DeploymentFilterState;
  /** What the entity picker offers. */
  readonly tree: EntityTree;
}
</script>

<script setup lang="ts">
import { ArrowDown, ListFilter } from "lucide-vue-next";

import type { DeploymentFilterState as Filter } from "@/domain/deployments";

import DeploymentFilterBar from "./DeploymentFilterBar.vue";

/**
 * The Deployments page's one filter, above everything it governs: a strip that holds under the top bar while the
 * measures and the deployments scroll beneath it, and says in words that it reaches only what lies below.
 */
defineOptions({ name: "DeploymentFilterScope" });
const props = defineProps<DeploymentFilterScopeProps>();
const emit = defineEmits<{ change: [filter: Partial<Filter>] }>();
</script>

<template>
  <div class="sticky top-1 z-sticky md:top-2">
    <section
      aria-labelledby="deployments-filter-heading"
      class="wh-glass min-w-0 rounded-2xl border border-(--wh-glass-edge) bg-(--wh-glass-fill) px-3 py-2.5 shadow-(--wh-glass-shadow) md:px-4 md:py-3"
    >
      <div class="mb-2 flex items-baseline justify-between gap-x-4">
        <h2 id="deployments-filter-heading" class="flex items-center gap-1.5 text-sm font-semibold">
          <ListFilter :size="14" aria-hidden="true" />Filter
        </h2>
        <p class="flex items-center gap-1 text-xs text-muted-foreground">
          <ArrowDown :size="12" aria-hidden="true" /><span class="md:hidden">Applies below</span
          ><span class="hidden md:inline">Applies to the measures and deployments below</span>
        </p>
      </div>
      <DeploymentFilterBar :filter="props.filter" :tree="props.tree" is-single-row @change="emit('change', $event)" />
    </section>
  </div>
</template>
