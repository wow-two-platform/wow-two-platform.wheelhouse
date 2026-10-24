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
  /** The deployments of a release the address chose: their rows are marked, and the first carries `data-selected-release`. */
  readonly selectedIds?: ReadonlySet<string>;
}
</script>

<script setup lang="ts">
import { ReleaseSets } from "@/domain/deployments";
import { computed } from "vue";
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
import { DeployState, Measures } from "@/domain/common";
import { DeploymentOutcome, DeploymentStates } from "@/domain/deployments";

import DeploymentStateChip from "../DeploymentStateChip.vue";
import ServiceStrip from "../ServiceStrip.vue";

/**
 * Lists deployments with how each went and what it changed; a row opens the deployment's own page. Each row carries
 * the state chip with its cause beneath it, and the service strip — one glyph per service in rollout order, the origin
 * ringed, in words on a phone. A deploy that changed nothing shows dimmed; the rows of a chosen release are marked.
 * Below `md` the SDK table stacks each row into a labelled card. Older deployments load a page at a time.
 */
defineOptions({ name: "DeploymentList" });
const props = withDefaults(defineProps<DeploymentListProps>(), {
  hasMore: false,
  isLoadingMore: false,
  isProductHidden: false,
  selectedIds: () => new Set<string>(),
});
const emit = defineEmits<{ more: [] }>();
const router = useRouter();
const rows = useSkeletonItems(() => props.items, 5);
/** The first chosen row, which the panel scrolls to. */
const firstSelected = computed(() => props.items?.find((item) => props.selectedIds.has(item.id))?.id ?? null);

/** A deployment's chip reading. @internal */
const readingOf = (item: DeploymentItem) => DeploymentStates.of(item);

/** Whether a deployment changed nothing, so its row reads dimmed. @internal */
const isUnchanged = (item: DeploymentItem | undefined) => item?.state === DeployState.NotApplied;

/** The changed services in words, for a deployment whose target reported no per-service results. @internal */
function changed(item: DeploymentItem): string {
  // A deploy that changed nothing touched no service, whatever its release would have changed.
  if (item.changedServices === null || isUnchanged(item)) return "—";
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
    <Table density="compact" is-hoverable>
      <TableHead>
        <TableRow>
          <TableHeaderCell>Release</TableHeaderCell>
          <TableHeaderCell v-if="!props.isProductHidden">Product</TableHeaderCell>
          <TableHeaderCell>Environment</TableHeaderCell>
          <TableHeaderCell>Services</TableHeaderCell>
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
          :class="[
            'cursor-pointer',
            isUnchanged(item) ? 'opacity-70' : '',
            item && props.selectedIds.has(item.id) ? 'bg-primary/10 shadow-[inset_3px_0_0_var(--color-primary)] hover:bg-primary/15' : '',
          ]"
          :aria-current="item && props.selectedIds.has(item.id) ? 'true' : undefined"
          :data-selected-release="item && item.id === firstSelected ? '' : undefined"
          @click="open(item, $event)"
        >
          <TableCell>
            <RouterLink
              v-if="item"
              :to="pathOf(item)"
              class="inline-flex min-h-11 items-center gap-2 font-mono text-xs font-medium hover:text-primary focus-visible:outline-primary md:min-h-0"
              :aria-label="`Deployment of ${ReleaseSets.text(item.release) || item.bundleId || 'an unknown release'} to ${item.targetId ?? 'a removed target'}`"
            >
              {{ ReleaseSets.text(item.release) || item.bundleId || "—" }}
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
            <template v-if="item">
              <ServiceStrip v-if="item.services" :services="item.services" />
              <span v-else :class="item.changedServices === null ? 'text-muted-foreground' : ''">{{ changed(item) }}</span>
            </template>
          </TableCell>
          <TableCell>
            <span v-if="item" class="block max-w-sm min-w-0">
              <DeploymentStateChip :reading="readingOf(item)" has-cause />
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
