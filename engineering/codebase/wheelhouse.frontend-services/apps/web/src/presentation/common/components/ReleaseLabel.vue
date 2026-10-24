<script lang="ts">
/** Defines one release label. */
export interface ReleaseLabelProps {
  /** The label as the runner keeps it: a set's joined label, `set-<12 hex>`, a release tag or a commit build. */
  readonly release: string | null | undefined;
  /** What shows when there is no label. */
  readonly fallback?: string;
  /** The chips' text size for a set: `sm` (12px) in rows and table heads, `md` (14px) on a card. */
  readonly size?: "sm" | "md";
}
</script>
<script setup lang="ts">
import { computed } from "vue";
import { Tag } from "@wow-two-beta/fe/presentation/display";
import { ReleaseSets } from "@/domain/deployments";

/**
 * A release label as people read it. A release set (decision 70) shows one chip per repository, its name muted and its
 * release emphasized, wrapping instead of truncating, with the joined label as its title; every other label shows as
 * it is, in the surrounding text's style.
 */
defineOptions({ name: "ReleaseLabel" });
const props = withDefaults(defineProps<ReleaseLabelProps>(), { fallback: "", size: "sm" });
defineSlots<{}>();

const parts = computed(() => ReleaseSets.parts(props.release));
</script>
<template>
  <span v-if="parts" class="flex min-w-0 flex-wrap gap-1" :title="props.release ?? undefined" data-release-set>
    <span class="sr-only">{{ ReleaseSets.text(props.release) }}</span>
    <Tag
      v-for="part in parts"
      :key="part.name"
      aria-hidden="true"
      class="gap-1 px-1.5 font-normal whitespace-nowrap"
      :class="props.size === 'md' ? 'text-sm' : 'text-xs'"
      ><span class="text-muted-foreground">{{ part.name }}</span>
      <span class="font-semibold tabular-nums">{{ part.release }}</span></Tag
    >
  </span>
  <span v-else :title="props.release ?? undefined">{{ props.release || props.fallback }}</span>
</template>
