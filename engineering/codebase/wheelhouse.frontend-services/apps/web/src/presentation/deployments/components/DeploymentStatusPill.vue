<script lang="ts">
import type { DeploymentWatch, JobStatus } from "@/domain/deployments";

/** Defines a deployment's status pill: what `DeploymentStatusRules` reads, and whether the cause is written out. */
export interface DeploymentStatusPillProps {
  readonly status: JobStatus;
  /** The display-safe explanation of its outcome. */
  readonly reason?: string | null | undefined;
  /** The watch after a successful rollout: a regression turns the success degraded. */
  readonly watch?: Pick<DeploymentWatch, "status" | "findings"> | null | undefined;
  /** The release a roll back restored, when known. */
  readonly restored?: string | null | undefined;
  /** Writes the cause after the pill — `rolled back to the previous release`, a failure's reason. Default `true`. */
  readonly hasCause?: boolean;
}
</script>

<script setup lang="ts">
import { computed } from "vue";

import { DeploymentStatusRules } from "@/domain/deployments";
import { StatusPill } from "@/presentation/common/components";

/**
 * A deployment's status as the console's status pill — the same icons, tones and words as an environment's pill — and
 * its cause after it: a roll back reads `degraded`, then `rolled back to the previous release`. Without the written
 * cause, the pill's title and its assistive name still carry it.
 */
defineOptions({ name: "DeploymentStatusPill" });
const props = withDefaults(defineProps<DeploymentStatusPillProps>(), { hasCause: true });
defineSlots<{}>();

const reading = computed(() => DeploymentStatusRules.of(props));
</script>

<template>
  <span class="inline-flex max-w-full min-w-0 flex-wrap items-center gap-x-2 gap-y-1">
    <StatusPill :state="reading.state" :label="reading.word" :title="reading.cause ?? undefined" />
    <span v-if="reading.cause && props.hasCause" class="min-w-0 text-xs break-words text-muted-foreground">{{
      reading.cause
    }}</span>
    <span v-else-if="reading.cause" class="sr-only">: {{ reading.cause }}</span>
  </span>
</template>
