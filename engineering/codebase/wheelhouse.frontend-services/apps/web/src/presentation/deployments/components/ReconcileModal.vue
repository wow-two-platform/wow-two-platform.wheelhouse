<script lang="ts">
import type { RolloutRecord } from "@/domain/deployments";

/** Defines the exact locked target and remote rollout the owner will acknowledge. */
export interface ReconcileModalProps {
  /** The literal runner target identifier. */
  readonly target: string;
  /** The active remote rollout, whose ID differs from its submission ID. */
  readonly active: RolloutRecord;
  /** The controlled dialog state. */
  readonly open: boolean;
}
</script>

<script setup lang="ts">
import { ReleaseSets } from "@/domain/deployments";
import { failureReason } from "@/integration/common";
import { watch } from "vue";

import { TypedConfirmModal } from "@wow-two-beta/fe/presentation/overlays";

import { useReconcileTarget } from "@/application/deployments";

/** Renders typed confirmation before acknowledging an inspected remote rollout. */
defineOptions({ name: "ReconcileModal" });
const props = defineProps<ReconcileModalProps>();
const emit = defineEmits<{ "update:open": [open: boolean] }>();
const reconcile = useReconcileTarget();

/** Resets the last refusal when the dialog, its target or its active rollout changes; the modal clears the typed ID. */
watch(
  () => [props.open, props.target, props.active.id],
  () => {
    reconcile.reset();
  },
);

/** Acknowledges the exact remote rollout only after matching the target name; the API receives no `confirm`. */
function confirm() {
  return reconcile.mutateAsync({
    target: props.target,
    job: props.active.id,
  });
}
</script>

<template>
  <TypedConfirmModal
    :open="props.open"
    :title="`Reconcile ${props.target}?`"
    description="Confirm after inspecting this target's containers and database schema. Reconciliation marks the rollout interrupted and clears its automatic rollback pointer. Containers and data stay unchanged."
    :expected="props.target"
    confirm-label="Reconcile"
    :error="failureReason(reconcile.error.value)"
    @update:open="emit('update:open', $event)"
    @confirm="confirm"
  >
    <p class="text-sm text-muted-foreground">
      Stopped rollout: {{ ReleaseSets.text(props.active.release) || props.active.id }}
      <span v-if="props.active.reason"> · {{ props.active.reason }}</span>
    </p>
  </TypedConfirmModal>
</template>
