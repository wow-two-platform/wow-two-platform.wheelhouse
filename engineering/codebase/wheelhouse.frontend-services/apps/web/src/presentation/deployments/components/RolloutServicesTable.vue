<script lang="ts">
import type { RolloutServiceRow } from "@/domain/deployments";

/** Defines the services of one rollout, each with the version it ran and the version the release brings. */
export interface RolloutServicesTableProps {
  /** The rows in rollout order: what changes first, then what keeps running, then what the release drops. */
  readonly rows: readonly RolloutServiceRow[];
  /** One line on what the rollout changes, beside the heading. */
  readonly summary?: string;
}
</script>

<script setup lang="ts">
import { computed, useId, type Component } from "vue";
import {
  CircleCheck,
  CircleDashed,
  CircleHelp,
  CircleMinus,
  CirclePlay,
  CircleStop,
  CircleX,
  Hourglass,
  LoaderCircle,
  RefreshCcwDot,
  TriangleAlert,
  Undo2,
} from "lucide-vue-next";

import {
  RolloutProgress,
  ServiceMark,
  ServiceMarkWords,
  type ServiceVersionCell,
} from "@/domain/deployments";

/**
 * Renders a rollout's services as a table: each service's current version and the next one, a mark beside each that
 * says how it fares — the old one stopped, the new one started and healthy, an untouched one kept running, a failure.
 * Every mark has its own shape and its words, both in the legend under the table and for assistive technology.
 */
defineOptions({ name: "RolloutServicesTable" });
const props = defineProps<RolloutServicesTableProps>();

/** Each mark's glyph: a shape of its own, so colour is never the only signal. @internal */
const MarkIcons: Readonly<Record<ServiceMark, Component>> = {
  running: CirclePlay,
  stopped: CircleStop,
  healthy: CircleCheck,
  unhealthy: TriangleAlert,
  starting: LoaderCircle,
  waiting: Hourglass,
  failed: CircleX,
  not_started: CircleDashed,
  rolled_back: Undo2,
  restored: RefreshCcwDot,
  removed: CircleMinus,
  unknown: CircleHelp,
};

/** Each mark's colour: green while a version serves, red for a failure, amber for what needs a look. @internal */
const MarkTones: Readonly<Record<ServiceMark, string>> = {
  running: "text-success",
  stopped: "text-muted-foreground",
  healthy: "text-success",
  unhealthy: "text-warning",
  starting: "text-primary",
  waiting: "text-muted-foreground",
  failed: "text-destructive",
  not_started: "text-warning",
  rolled_back: "text-warning",
  restored: "text-warning",
  removed: "text-muted-foreground",
  unknown: "text-muted-foreground",
};

const marks = computed(() => RolloutProgress.marks(props.rows));
const headingId = useId();
/** A starting version's glyph turns slowly in its cell, and holds still under reduced motion and in the legend. @internal */
const Spinning =
  "animate-spin [animation-duration:2s] motion-reduce:animate-none";

/** A cell's tooltip: its version and how it fares. @internal */
function describe(cell: ServiceVersionCell): string {
  return [cell.version, cell.mark ? ServiceMarkWords[cell.mark] : null]
    .filter(Boolean)
    .join(" · ");
}
</script>

<template>
  <section class="flex flex-col gap-2" :aria-labelledby="headingId">
    <div
      class="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-0.5"
    >
      <h3 :id="headingId" class="text-sm font-semibold">Services</h3>
      <p v-if="props.summary" class="text-xs text-muted-foreground">
        {{ props.summary }}
      </p>
    </div>
    <div class="overflow-x-auto rounded-xl border border-border">
      <table class="w-full border-collapse text-sm">
        <caption class="sr-only">
          Each service's current version and the version the release brings
        </caption>
        <thead class="bg-muted/50 text-xs text-muted-foreground">
          <tr>
            <th scope="col" class="px-3 py-1.5 text-start font-medium">
              Service
            </th>
            <th scope="col" class="px-3 py-1.5 text-start font-medium">
              Current
            </th>
            <th scope="col" class="px-3 py-1.5 text-start font-medium">Next</th>
          </tr>
        </thead>
        <tbody class="divide-y divide-border">
          <tr
            v-for="row in props.rows"
            :key="row.service"
            :data-changed="row.changed"
          >
            <th
              scope="row"
              :class="[
                'px-3 py-1.5 text-start font-mono text-xs font-medium',
                row.changed ? 'text-foreground' : 'text-muted-foreground',
              ]"
            >
              {{ row.service }}
            </th>
            <td
              v-for="(cell, side) in [row.current, row.next]"
              :key="side"
              class="px-3 py-1.5"
            >
              <span
                v-if="cell"
                class="inline-flex items-center gap-1.5"
                :title="describe(cell)"
              >
                <component
                  :is="MarkIcons[cell.mark]"
                  v-if="cell.mark"
                  :size="14"
                  :class="[
                    'shrink-0',
                    MarkTones[cell.mark],
                    cell.mark === ServiceMark.Starting ? Spinning : '',
                  ]"
                  aria-hidden="true"
                />
                <span
                  :class="[
                    'font-mono text-xs',
                    row.changed ? '' : 'text-muted-foreground',
                  ]"
                  >{{
                    cell.version ??
                    (cell.mark === ServiceMark.Removed ? "removed" : "—")
                  }}</span
                >
                <span v-if="cell.mark" class="sr-only"
                  >, {{ ServiceMarkWords[cell.mark] }}</span
                >
              </span>
              <span v-else class="text-xs text-muted-foreground"
                >unchanged</span
              >
            </td>
          </tr>
        </tbody>
      </table>
    </div>
    <ul
      v-if="marks.length"
      class="flex flex-wrap gap-x-3 gap-y-1 text-xs text-muted-foreground"
      aria-label="What the marks mean"
    >
      <li
        v-for="mark in marks"
        :key="mark"
        class="inline-flex items-center gap-1"
      >
        <component
          :is="MarkIcons[mark]"
          :size="12"
          :class="['shrink-0', MarkTones[mark]]"
          aria-hidden="true"
        />{{ ServiceMarkWords[mark] }}
      </li>
    </ul>
  </section>
</template>
