<script lang="ts">
import type { PolicyDiffHunk } from "@/domain/infrastructure";

/** Defines a unified view of the policy diff's hunks. */
export interface PolicyDiffViewProps {
  readonly hunks: readonly PolicyDiffHunk[];
}
</script>

<script setup lang="ts">
import { PolicyDiff } from "@/domain/infrastructure";

/**
 * Draws the live policy against the rendered one as a unified diff: each hunk's header, then its lines with their old
 * and new numbers, removed lines red and added lines green, each marked too, so colour is never the only signal. Long
 * lines scroll sideways inside the view.
 */
defineOptions({ name: "PolicyDiffView" });
const props = defineProps<PolicyDiffViewProps>();
defineSlots<{}>();

const rowClass = {
  same: "",
  add: "bg-success-soft text-success-soft-foreground",
  remove: "bg-destructive-soft text-destructive-soft-foreground",
} as const;
</script>

<template>
  <div class="max-h-[50vh] overflow-auto rounded-xl border border-border bg-card font-mono text-xs leading-5">
    <table class="w-full min-w-max border-collapse" aria-label="Policy changes">
      <tbody v-for="(hunk, index) in props.hunks" :key="index">
        <tr class="bg-muted text-muted-foreground">
          <td colspan="4" class="px-3 py-1">{{ PolicyDiff.header(hunk) }}</td>
        </tr>
        <tr v-for="(line, row) in hunk.lines" :key="row" :class="rowClass[line.kind]">
          <td class="w-10 px-2 text-right text-muted-foreground tabular-nums select-none">{{ line.old ?? "" }}</td>
          <td class="w-10 px-2 text-right text-muted-foreground tabular-nums select-none">{{ line.new ?? "" }}</td>
          <td class="w-4 px-1 text-center select-none" aria-hidden="true">{{ PolicyDiff.marker(line.kind) }}</td>
          <td class="pe-3 whitespace-pre">
            <span v-if="line.kind !== 'same'" class="sr-only">{{ PolicyDiff.word(line.kind) }}: </span>{{ line.text }}
          </td>
        </tr>
      </tbody>
    </table>
  </div>
</template>
