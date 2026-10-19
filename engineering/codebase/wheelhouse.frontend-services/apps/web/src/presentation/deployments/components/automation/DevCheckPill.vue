<script lang="ts">
import type { DeploymentVerification } from "@/domain/deployments";

/** Defines a release's dev check pill. */
export interface DevCheckPillProps {
  /** The check's result on the dev rollout that proved the release; null when it ran none. */
  readonly verification: DeploymentVerification | null;
  /** Whether the release declares a check; null when its bundle is unknown. */
  readonly verifies: boolean | null;
  /** Writes after the pill that auto-promote waits for the check, when it does. Default `false`. */
  readonly hasHold?: boolean;
  /** Names the check on the pill (`Dev check failed`); without it, where a label already names it, the word alone. */
  readonly hasLabel?: boolean;
}
</script>

<script setup lang="ts">
import { computed } from "vue";

import { DevCheckRules } from "@/domain/deployments";
import { StatusPill } from "@/presentation/common/components";

/**
 * A release's end-to-end check on dev as the console's status pill — `Dev check passed`, `failed`, `not run` or `no check
 * declared` — with what the check said in its title. Auto-promote takes the release only once its check passed, or when
 * it declares none; with `hasHold` the row says so when the check holds it.
 */
defineOptions({ name: "DevCheckPill" });
const props = withDefaults(defineProps<DevCheckPillProps>(), { hasHold: false, hasLabel: true });
defineSlots<{}>();

const reading = computed(() => DevCheckRules.of(props));
const title = computed(() =>
  [reading.value.cause, reading.value.holdsAutoPromote ? "Auto-promote waits for a passed check." : null]
    .filter(Boolean)
    .join(" "),
);
</script>

<template>
  <span class="inline-flex max-w-full min-w-0 flex-wrap items-center gap-x-2 gap-y-1">
    <StatusPill
      :state="reading.status"
      :label="props.hasLabel ? 'Dev check' : reading.word"
      :detail="props.hasLabel ? reading.word : null"
      :title="title || undefined"
    />
    <span v-if="props.hasHold && reading.holdsAutoPromote" class="text-xs text-muted-foreground"
      >Auto-promote waits for a passed check</span
    >
  </span>
</template>
