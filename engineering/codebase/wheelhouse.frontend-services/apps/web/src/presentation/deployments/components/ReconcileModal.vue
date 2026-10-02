<script lang="ts">
import type { RolloutRecord } from "@/domain/deployments";

/** Defines the exact locked target and remote rollout the operator will acknowledge. */
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
import { failureReason } from "@/integration/common";
import { computed, ref, watch } from "vue";

import { Button } from "@wow-two-beta/ui-vue/presentation/actions";
import { Alert } from "@wow-two-beta/ui-vue/presentation/feedback";
import { Field, TextInput } from "@wow-two-beta/ui-vue/presentation/forms";
import {
  AlertModal,
  AlertModalContent,
  ModalBody,
  ModalDescription,
  ModalFooter,
  ModalHeader,
  ModalTitle,
} from "@wow-two-beta/ui-vue/presentation/overlays";

import { useReconcileTarget } from "@/application/deployments";

/** Renders typed confirmation before acknowledging an inspected remote rollout. */
defineOptions({ name: "ReconcileModal" });
const props = defineProps<ReconcileModalProps>();
const emit = defineEmits<{ "update:open": [open: boolean] }>();
const reconcile = useReconcileTarget();
const typed = ref("");
const confirmed = computed(() => typed.value === props.target);

/** Resets confirmation when its target or active rollout changes. */
watch(
  () => [props.open, props.target, props.active.id],
  () => {
    typed.value = "";
    reconcile.reset();
  },
);

/** Requests dismissal without acknowledging anything. */
function close(open: boolean): void {
  if (reconcile.loading.value) return;
  emit("update:open", open);
}

/** Acknowledges the exact remote rollout only after matching the target name. */
async function confirm(): Promise<void> {
  if (!confirmed.value || reconcile.loading.value) return;
  const target = props.target;
  const job = props.active.id;
  const result = await reconcile.mutateAsync({
    target,
    job,
  });
  if (result.ok && props.target === target && props.active.id === job)
    emit("update:open", false);
}
</script>

<template>
  <AlertModal
    :open="props.open"
    :can-dismiss-on-escape="!reconcile.loading.value"
    @update:open="close"
  >
    <AlertModalContent class="flex max-h-[calc(100dvh-2rem)] flex-col">
      <ModalHeader>
        <ModalTitle>Reconcile {{ props.target }}?</ModalTitle>
        <ModalDescription>
          Confirm after inspecting this target's containers and database schema.
          Reconciliation marks the rollout interrupted and clears its automatic
          rollback pointer. Containers and data stay unchanged.
        </ModalDescription>
      </ModalHeader>
      <ModalBody class="-mx-1 min-h-0 flex-1 overflow-y-auto px-1 flex flex-col gap-4">
        <p class="text-sm text-muted-foreground">
          Stopped rollout: {{ props.active.release ?? props.active.id }}
          <span v-if="props.active.reason"> · {{ props.active.reason }}</span>
        </p>
        <Field :label="`Type ${props.target} to confirm`">
          <TextInput
            v-model="typed"
            :disabled="reconcile.loading.value"
            autocomplete="off"
          />
        </Field>
        <Alert
          v-if="reconcile.error.value"
          severity="danger"
          :description="failureReason(reconcile.error.value)"
        />
      </ModalBody>
      <ModalFooter>
        <Button
          variant="outline"
          tone="neutral"
          :is-disabled="reconcile.loading.value"
          @click="close(false)"
        >
          Cancel
        </Button>
        <Button
          variant="solid"
          tone="danger"
          :is-disabled="!confirmed"
          :is-loading="reconcile.loading.value"
          @click="confirm"
          >Reconcile</Button
        >
      </ModalFooter>
    </AlertModalContent>
  </AlertModal>
</template>
