<script lang="ts">
import type { RouteLocationRaw } from "vue-router";

import type { ImpactLine } from "@/domain/deployments";

/** Defines a deployment's impact list. */
export interface ImpactListProps {
  /** The origins, each with the effects likely via it, then any other service that is not fine. */
  readonly lines: readonly ImpactLine[];
  /** Where the service map opens, on the origin; null hides the action. */
  readonly map?: RouteLocationRaw | null;
}
</script>

<script setup lang="ts">
import { RouterLink } from "vue-router";
import { Crosshair, Network } from "lucide-vue-next";

import { Button } from "@wow-two-beta/fe/presentation/actions";

import { ServiceResultTones } from "@/domain/common";
import { ServiceResultIcons } from "@/presentation/common/components";

/**
 * A deployment's impact as a list — what a phone shows in place of the service map: each service where the trouble
 * started, then the services it likely hurt indented beneath it along a declared path (never an observed call), then
 * any other service not fine. "Open map" opens the product's service map on the origin, one tap away.
 */
defineOptions({ name: "ImpactList" });
const props = withDefaults(defineProps<ImpactListProps>(), { map: null });
defineSlots<{}>();

/** A line's glyph colour by its result's tone. @internal */
const ToneText: Readonly<Record<string, string>> = {
  calm: "text-success",
  info: "text-info",
  warning: "text-warning",
  danger: "text-destructive",
  quiet: "text-muted-foreground",
  empty: "text-subtle-foreground",
};
</script>

<template>
  <div class="flex min-w-0 flex-col gap-3">
    <ul class="flex flex-col gap-2" aria-label="Impact">
      <li
        v-for="line in props.lines"
        :key="line.service"
        :class="[
          'flex min-w-0 items-start gap-2 text-sm',
          line.role === 'effect' ? 'ms-3 border-s-2 border-dashed border-destructive/50 ps-3' : '',
        ]"
      >
        <component
          :is="ServiceResultIcons[line.result]"
          :size="16"
          aria-hidden="true"
          :class="['mt-0.5 shrink-0', ToneText[ServiceResultTones[line.result]]]"
        />
        <span class="min-w-0">
          <span class="flex flex-wrap items-center gap-1.5">
            <span class="font-mono font-medium">{{ line.service }}</span>
            <span
              v-if="line.role === 'origin'"
              class="inline-flex items-center gap-1 rounded-full border border-destructive/45 px-1.5 text-[0.6875rem] text-destructive-soft-foreground"
              ><Crosshair :size="10" aria-hidden="true" />origin</span
            >
          </span>
          <span class="block text-xs text-muted-foreground">{{ line.text }}</span>
        </span>
      </li>
    </ul>
    <Button v-if="props.map" as-child variant="outline" tone="neutral" size="sm" class="self-start max-md:min-h-11">
      <RouterLink :to="props.map"><Network :size="14" aria-hidden="true" class="me-1.5" />Open map</RouterLink>
    </Button>
  </div>
</template>
