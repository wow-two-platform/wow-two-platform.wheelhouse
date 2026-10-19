<script lang="ts">
import type { DeploymentItem } from "@/domain/deployments";

/** Defines a list of deployments: a table on desktop, one card per deployment on a phone. */
export interface DeploymentListProps {
  /** The deployments, newest first; undefined until the first read, which draws placeholder rows. */
  readonly items: readonly DeploymentItem[] | undefined;
  /** Product names by slug; a product the console does not know shows its slug. */
  readonly productNames: ReadonlyMap<string, string>;
  /** Whether an older page can be read. */
  readonly hasMore?: boolean;
  /** Whether the older page is being read. */
  readonly isLoadingMore?: boolean;
  /** Hides the product column, as a product's own tab does. */
  readonly isProductHidden?: boolean;
  /**
   * `table` (default): the List view's table, stacked into cards below `md`. `rows`: the same facts as one compact row
   * per deployment, for a narrow column such as the calendar's day panel.
   */
  readonly layout?: "table" | "rows";
}
</script>

<script setup lang="ts">
import { RouterLink, useRouter } from "vue-router";

import { Button } from "@wow-two-beta/fe/presentation/actions";
import {
  Badge,
  RelativeTime,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
} from "@wow-two-beta/fe/presentation/display";
import { useSkeletonItems } from "@wow-two-beta/fe/presentation/feedback";

import { ConsolePaths } from "@/application/navigation";
import { Measures } from "@/domain/common";
import { DeploymentOutcome } from "@/domain/deployments";

import DeploymentStatusPill from "../DeploymentStatusPill.vue";

/**
 * Lists deployments with what changed and how it went; a row opens the deployment's own page. Below `md` the SDK
 * table stacks each row into a labelled card; the `rows` layout says the same in one compact row each, for a narrow
 * column. Older deployments load a page at a time.
 */
defineOptions({ name: "DeploymentList" });
const props = withDefaults(defineProps<DeploymentListProps>(), {
  hasMore: false,
  isLoadingMore: false,
  isProductHidden: false,
  layout: "table",
});
const emit = defineEmits<{ more: [] }>();
const router = useRouter();
const rows = useSkeletonItems(() => props.items, 5);

function changed(item: DeploymentItem): string {
  if (item.changedServices === null) return "unknown";
  return item.changedServices.length ? item.changedServices.join(", ") : "none";
}

function pathOf(item: DeploymentItem): string {
  return `${ConsolePaths.deployments}/${encodeURIComponent(item.id)}`;
}

function productOf(item: DeploymentItem): string {
  return item.product ? (props.productNames.get(item.product) ?? item.product) : "—";
}

function durationOf(item: DeploymentItem): string {
  return item.durationSeconds !== null
    ? Measures.duration(item.durationSeconds)
    : item.outcome === DeploymentOutcome.Pending
      ? "running"
      : "—";
}

function open(item: DeploymentItem | undefined, event: MouseEvent): void {
  // A click on the row's own link already navigates; a selection drag reads text rather than opening.
  if (!item || (event.target as HTMLElement | null)?.closest("a,button") || window.getSelection()?.toString()) return;
  void router.push(pathOf(item));
}
</script>

<template>
  <div class="flex min-w-0 flex-col gap-3">
    <ul v-if="props.layout === 'rows'" class="flex min-w-0 flex-col divide-y divide-border" aria-label="Deployments">
      <li v-for="(item, index) in rows" :key="item?.id ?? `placeholder-${index}`" class="min-w-0">
        <RouterLink
          v-if="item"
          :to="pathOf(item)"
          class="-mx-2 flex min-h-11 min-w-0 flex-col gap-1 rounded-lg px-2 py-2.5 hover:bg-muted/60 focus-visible:outline-2 focus-visible:outline-primary"
          :aria-label="`Deployment of ${item.release ?? item.bundleId ?? 'an unknown release'} to ${item.targetId ?? 'a removed target'}`"
        >
          <span class="flex min-w-0 items-center justify-between gap-3">
            <span class="flex min-w-0 items-center gap-2">
              <span class="truncate font-mono text-xs font-medium">{{ item.release ?? item.bundleId ?? "—" }}</span>
              <Badge v-if="item.kind === 'candidate'" variant="neutral">build</Badge>
              <Badge :variant="item.environment === 'prod' ? 'info' : 'neutral'">{{ item.environment ?? item.targetId ?? "—" }}</Badge>
            </span>
            <span class="shrink-0 font-mono text-xs tabular-nums text-muted-foreground">{{ durationOf(item) }}</span>
          </span>
          <span class="min-w-0 text-sm">
            <DeploymentStatusPill :status="item.status" :reason="item.reason" :watch="item.watch" />
            <span v-if="item.warnings.length && item.outcome === DeploymentOutcome.Succeeded" class="mt-0.5 block text-xs text-warning">
              {{ item.warnings.length }} {{ item.warnings.length === 1 ? "warning" : "warnings" }}
            </span>
          </span>
          <span class="truncate text-xs text-muted-foreground">
            <template v-if="!props.isProductHidden">{{ productOf(item) }} · </template
            ><RelativeTime :value="item.startedAt ?? item.submittedAt" /> · {{ item.actor ?? "—" }}<template
              v-if="item.changedServices?.length"
            >
              · <span class="font-mono">{{ item.changedServices.join(", ") }}</span></template
            >
          </span>
        </RouterLink>
        <span v-else class="block py-2.5" aria-hidden="true"><span class="block h-14 animate-pulse rounded-lg bg-muted motion-reduce:animate-none" /></span>
      </li>
    </ul>
    <Table v-else density="compact" is-hoverable>
      <TableHead>
        <TableRow>
          <TableHeaderCell>Release</TableHeaderCell>
          <TableHeaderCell v-if="!props.isProductHidden">Product</TableHeaderCell>
          <TableHeaderCell>Environment</TableHeaderCell>
          <TableHeaderCell>Services changed</TableHeaderCell>
          <TableHeaderCell>Status</TableHeaderCell>
          <TableHeaderCell>Started</TableHeaderCell>
          <TableHeaderCell>Duration</TableHeaderCell>
          <TableHeaderCell>Actor</TableHeaderCell>
        </TableRow>
      </TableHead>
      <TableBody>
        <TableRow
          v-for="(item, index) in rows"
          :key="item?.id ?? `placeholder-${index}`"
          class="cursor-pointer"
          @click="open(item, $event)"
        >
          <TableCell>
            <RouterLink
              v-if="item"
              :to="pathOf(item)"
              class="inline-flex min-h-11 items-center gap-2 font-mono text-xs font-medium hover:text-primary focus-visible:outline-primary md:min-h-0"
              :aria-label="`Deployment of ${item.release ?? item.bundleId ?? 'an unknown release'} to ${item.targetId ?? 'a removed target'}`"
            >
              {{ item.release ?? item.bundleId ?? "—" }}
              <Badge v-if="item.kind === 'candidate'" variant="neutral">build</Badge>
            </RouterLink>
          </TableCell>
          <TableCell v-if="!props.isProductHidden" class="text-sm">
            <template v-if="item">{{ productOf(item) }}</template>
          </TableCell>
          <TableCell>
            <Badge v-if="item" :variant="item.environment === 'prod' ? 'info' : 'neutral'">{{
              item.environment ?? item.targetId ?? "—"
            }}</Badge>
          </TableCell>
          <TableCell class="font-mono text-xs">
            <span v-if="item" :class="item.changedServices === null ? 'text-muted-foreground' : ''">{{ changed(item) }}</span>
          </TableCell>
          <TableCell>
            <span v-if="item" class="block min-w-0">
              <DeploymentStatusPill :status="item.status" :reason="item.reason" :watch="item.watch" />
              <span v-if="item.warnings.length && item.outcome === DeploymentOutcome.Succeeded" class="mt-1 block text-xs text-warning">
                {{ item.warnings.length }} {{ item.warnings.length === 1 ? "warning" : "warnings" }}
              </span>
            </span>
          </TableCell>
          <TableCell class="whitespace-nowrap text-xs text-muted-foreground">
            <RelativeTime v-if="item" :value="item.startedAt ?? item.submittedAt" />
          </TableCell>
          <TableCell class="whitespace-nowrap font-mono text-xs tabular-nums">
            <template v-if="item">{{ durationOf(item) }}</template>
          </TableCell>
          <TableCell class="whitespace-nowrap text-xs">{{ item ? (item.actor ?? "—") : "" }}</TableCell>
        </TableRow>
      </TableBody>
    </Table>
    <div v-if="props.hasMore" class="flex justify-center">
      <Button variant="outline" tone="neutral" size="sm" :is-loading="props.isLoadingMore" class="max-md:min-h-11" @click="emit('more')"
        >Load older deployments</Button
      >
    </div>
  </div>
</template>
