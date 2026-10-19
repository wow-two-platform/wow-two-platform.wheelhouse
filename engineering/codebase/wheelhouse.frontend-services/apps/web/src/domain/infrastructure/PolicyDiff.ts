import type { PolicyDiffHunk, PolicyDiffLine } from './Tailnet';

const Markers: Readonly<Record<PolicyDiffLine['kind'], string>> = { same: ' ', add: '+', remove: '-' };
const Words: Readonly<Record<PolicyDiffLine['kind'], string>> = { same: 'unchanged', add: 'added', remove: 'removed' };

/** Pure display rules over the policy diff's hunks, drawn as a unified diff. */
export const PolicyDiff = {
  /** The line's unified-diff marker. */
  marker: (kind: PolicyDiffLine['kind']): string => Markers[kind],

  /** The line's kind in words, for assistive technology. */
  word: (kind: PolicyDiffLine['kind']): string => Words[kind],

  /** How many lines of the live policy the hunk covers. */
  oldCount: (hunk: PolicyDiffHunk): number => hunk.lines.filter((line) => line.kind !== 'add').length,

  /** How many lines of the rendered policy the hunk covers. */
  newCount: (hunk: PolicyDiffHunk): number => hunk.lines.filter((line) => line.kind !== 'remove').length,

  /** The hunk's header: `@@ -12,7 +12,9 @@`. */
  header: (hunk: PolicyDiffHunk): string =>
    `@@ -${hunk.oldStart},${PolicyDiff.oldCount(hunk)} +${hunk.newStart},${PolicyDiff.newCount(hunk)} @@`,

  /** Lines added and removed across the hunks. */
  counts: (hunks: readonly PolicyDiffHunk[]): { added: number; removed: number } => {
    let added = 0;
    let removed = 0;
    for (const hunk of hunks)
      for (const line of hunk.lines) {
        if (line.kind === 'add') added++;
        else if (line.kind === 'remove') removed++;
      }
    return { added, removed };
  },

  /** The change in words: `3 lines added, 1 removed`, or `No change`. */
  summary: (hunks: readonly PolicyDiffHunk[]): string => {
    const { added, removed } = PolicyDiff.counts(hunks);
    if (!added && !removed) return 'No change';
    const parts = [
      added ? `${added} ${added === 1 ? 'line' : 'lines'} added` : '',
      removed ? `${removed}${added ? '' : removed === 1 ? ' line' : ' lines'} removed` : '',
    ].filter(Boolean);
    return parts.join(', ');
  },
} as const;
