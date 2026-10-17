<script lang="ts">
import type { DeploymentJob } from "@/domain/deployments";

/** Defines recent submissions and optional inspector or redeploy actions. */
export interface HistoryTableProps {
  /** The observed submissions, in their supplied order; undefined until the first read, which draws placeholder rows. */
  readonly jobs: readonly DeploymentJob[] | undefined;
  /** Opens deployment confirmation for an eligible successful submission. */
  readonly onRedeploy?: (job: DeploymentJob) => void;
  /** Opens the selected submission's inspector. */
  readonly onSelect?: (job: DeploymentJob) => void;
}
</script>

<script setup lang="ts">
import { RotateCcw } from "lucide-vue-next";

import { Button } from "@wow-two-beta/fe/presentation/actions";
import {
  RelativeTime,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
} from "@wow-two-beta/fe/presentation/display";
import { useSkeletonItems } from "@wow-two-beta/fe/presentation/feedback";

import { JobStatus } from "@/domain/deployments";

import JobStatusIndicator from "./JobStatusIndicator.vue";

/** Renders observed deployment history with optional explicit actions; inside a first-loading region its rows draw skeletons. */
defineOptions({ name: "HistoryTable" });
const props = defineProps<HistoryTableProps>();
const rows = useSkeletonItems(() => props.jobs, 5);
</script>

<template>
  <Table
    density="compact"
    is-hoverable
    has-sticky-header
    container-class-name="max-h-[28rem] overflow-auto"
  >
    <TableHead>
      <TableRow>
        <TableHeaderCell>Submitted</TableHeaderCell
        ><TableHeaderCell>Target · release</TableHeaderCell>
        <TableHeaderCell>Status</TableHeaderCell
        ><TableHeaderCell>Actor</TableHeaderCell>
        <TableHeaderCell v-if="props.onRedeploy"
          ><span class="sr-only">Actions</span></TableHeaderCell
        >
      </TableRow>
    </TableHead>
    <TableBody>
      <TableRow v-for="(job, index) in rows" :key="job?.id ?? `placeholder-${index}`">
        <!-- A placeholder row leaves its cells empty, so each draws one bar. -->
        <TableCell class="whitespace-nowrap text-xs text-muted-foreground">
          <RelativeTime v-if="job?.submittedAt" :value="job.submittedAt" />
          <template v-else-if="job">—</template>
        </TableCell>
        <TableCell class="font-mono text-xs">
          <template v-if="job">
            <button
              v-if="props.onSelect"
              type="button"
              class="text-left hover:text-primary focus-visible:outline-primary"
              :aria-label="`Inspect deployment ${job.id}`"
              @click="props.onSelect(job)"
            >
              <span class="block">{{ job.targetId ?? "—" }}</span>
              <span class="mt-1 block text-muted-foreground">{{
                job.release ?? job.bundleId ?? "—"
              }}</span>
            </button>
            <span v-else class="block min-w-0">
              <span class="block">{{ job.targetId ?? "—" }}</span>
              <span class="mt-1 block text-muted-foreground">{{
                job.release ?? job.bundleId ?? "—"
              }}</span>
            </span>
          </template>
        </TableCell>
        <TableCell
          ><JobStatusIndicator v-if="job" :status="job.status" :reason="job.reason"
        /></TableCell>
        <TableCell class="text-xs">{{ job ? (job.actor ?? "—") : "" }}</TableCell>
        <TableCell v-if="props.onRedeploy" label="" class="justify-end text-right">
          <Button
            v-if="
              job && job.status === JobStatus.Succeeded && job.targetId && job.bundleId
            "
            variant="ghost"
            tone="neutral"
            size="sm"
            :aria-label="`Redeploy ${job.release ?? job.bundleId}`"
            title="Redeploy this release"
            @click="props.onRedeploy(job)"
          >
            <template #leading><RotateCcw :size="14" /></template>
          </Button>
        </TableCell>
      </TableRow>
    </TableBody>
  </Table>
</template>
