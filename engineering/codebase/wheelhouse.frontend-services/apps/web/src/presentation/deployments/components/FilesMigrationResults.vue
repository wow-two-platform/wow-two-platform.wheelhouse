<script lang="ts">
import type { FilesMigrationRun } from "@/domain/deployments/controls";

/** Defines what a files migration job kept: the per-source results and the import they went into. */
export interface FilesMigrationResultsProps {
  /** The job's `filesMigration`; null or without results renders nothing. */
  readonly run: FilesMigrationRun | null | undefined;
  /** The source ids in the order the job ran them; results of any other source follow. */
  readonly order?: readonly string[];
}
</script>

<script setup lang="ts">
import { computed, useId } from "vue";

import { CopyButton } from "@wow-two-beta/fe/presentation/actions";

import { Measures } from "@/domain/common";
import { FilesMigrations } from "@/domain/deployments/controls";

/**
 * Renders a files migration's results: the import id with a copy button (a verify, or a push that resumes, names it),
 * then a table with each finished source's files, bytes, skipped and failed counts and the total beneath. A failed count
 * reads in the danger colour; the import id is the only value the job keeps besides the counts.
 */
defineOptions({ name: "FilesMigrationResults" });
const props = withDefaults(defineProps<FilesMigrationResultsProps>(), { order: () => [] });
const headingId = useId();
const rows = computed(() => FilesMigrations.rows(props.run, props.order));
const total = computed(() => FilesMigrations.total(props.run));
</script>

<template>
  <section v-if="rows.length || props.run?.import" class="flex flex-col gap-3" :aria-labelledby="headingId">
    <h3 :id="headingId" class="sr-only">Files migration results</h3>
    <div v-if="props.run?.import" class="flex flex-wrap items-center gap-2 text-sm">
      <span class="text-muted-foreground">Import</span>
      <code class="min-w-0 font-mono text-xs break-all select-all">{{ props.run.import }}</code>
      <CopyButton
        :text="props.run.import"
        size="sm"
        variant="outline"
        tone="neutral"
        shape="square"
        class="max-md:size-11"
        aria-label="Copy the import id"
        copied-aria-label="Import id copied"
      />
    </div>
    <div v-if="rows.length" class="overflow-x-auto rounded-xl border border-border">
      <table class="w-full border-collapse text-sm">
        <caption class="sr-only">
          Each source's files, bytes, skipped and failed counts
        </caption>
        <thead class="bg-muted/50 text-xs text-muted-foreground">
          <tr>
            <th scope="col" class="px-3 py-1.5 text-start font-medium">Source</th>
            <th scope="col" class="px-3 py-1.5 text-end font-medium">Files</th>
            <th scope="col" class="px-3 py-1.5 text-end font-medium">Size</th>
            <th scope="col" class="px-3 py-1.5 text-end font-medium">Skipped</th>
            <th scope="col" class="px-3 py-1.5 text-end font-medium">Failed</th>
          </tr>
        </thead>
        <tbody class="divide-y divide-border">
          <tr v-for="row in rows" :key="row.source">
            <th scope="row" class="px-3 py-1.5 text-start font-mono text-xs font-medium">{{ row.source }}</th>
            <td class="px-3 py-1.5 text-end tabular-nums">{{ row.result.files }}</td>
            <td class="px-3 py-1.5 text-end tabular-nums" :title="`${row.result.bytes} bytes`">
              {{ Measures.bytes(row.result.bytes) }}
            </td>
            <td class="px-3 py-1.5 text-end tabular-nums">{{ row.result.skipped }}</td>
            <td :class="['px-3 py-1.5 text-end tabular-nums', row.result.failed > 0 ? 'font-medium text-destructive' : '']">
              {{ row.result.failed }}
            </td>
          </tr>
        </tbody>
        <tfoot v-if="rows.length > 1" class="border-t border-border bg-muted/30 text-xs font-medium">
          <tr>
            <th scope="row" class="px-3 py-1.5 text-start">Total</th>
            <td class="px-3 py-1.5 text-end tabular-nums">{{ total.files }}</td>
            <td class="px-3 py-1.5 text-end tabular-nums" :title="`${total.bytes} bytes`">{{ Measures.bytes(total.bytes) }}</td>
            <td class="px-3 py-1.5 text-end tabular-nums">{{ total.skipped }}</td>
            <td :class="['px-3 py-1.5 text-end tabular-nums', total.failed > 0 ? 'text-destructive' : '']">{{ total.failed }}</td>
          </tr>
        </tfoot>
      </table>
    </div>
  </section>
</template>
