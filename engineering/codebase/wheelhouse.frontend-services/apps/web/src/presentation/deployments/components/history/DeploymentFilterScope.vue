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
import { ListFilter } from "lucide-vue-next";

import type { DeploymentFilterState as Filter } from "@/domain/deployments";
import { InfoPopover, type InfoSection } from "@/presentation/common/components";

import DeploymentFilterBar from "./DeploymentFilterBar.vue";

/** What the filter's info button explains: its reach, and what the outcome choice changes. @internal */
const Sections: readonly InfoSection[] = [
  { key: "reach", title: "What it filters" },
  { key: "outcome", title: "Status" },
];

/**
 * The Deployments page's one filter, above everything it governs: a strip that holds under the top bar while the
 * measures and the deployments scroll beneath it. Its reach — only what lies below — sits behind the info button.
 */
defineOptions({ name: "DeploymentFilterScope" });
const props = defineProps<DeploymentFilterScopeProps>();
const emit = defineEmits<{ change: [filter: Partial<Filter>] }>();
</script>

<template>
  <div class="sticky top-1 z-sticky md:top-2">
    <section
      aria-labelledby="deployments-filter-heading"
      data-info-scope
      class="wh-glass min-w-0 rounded-2xl border border-(--wh-glass-edge) bg-(--wh-glass-fill) px-3 py-2.5 shadow-(--wh-glass-shadow) md:px-4 md:py-3"
    >
      <div class="mb-2 flex items-center gap-1">
        <h2 id="deployments-filter-heading" class="flex items-center gap-1.5 text-sm font-semibold">
          <ListFilter :size="14" aria-hidden="true" />Filter
        </h2>
        <InfoPopover title="Filter" :sections="Sections">
          <template #reach>
            <p>
              The measures and the deployments below follow it. Issues, Running now and Ready to promote above always
              cover every product.
            </p>
          </template>
          <template #outcome>
            <p>
              Needs action (partial, failed), Look (degraded, rolled back) or Succeeded narrows the calendar and the list
              to deployments needing that much attention. The measures count every state.
            </p>
          </template>

        </InfoPopover>
      </div>
      <DeploymentFilterBar :filter="props.filter" :tree="props.tree" is-single-row @change="emit('change', $event)" />
    </section>
  </div>
</template>
