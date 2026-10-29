<script lang="ts">
import type { ControlRequest } from "@/domain/deployments/controls";

/** The control the owner asked for and whether its confirmation is open. */
export interface ControlConfirmModalProps {
  /** The stop, start, restart or database reset to confirm; null while none is asked for. */
  readonly request: ControlRequest | null;
  /** The controlled dialog state. */
  readonly open: boolean;
}
</script>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { Eraser, Pause, Play, RotateCw } from "lucide-vue-next";

import { Field, TextInput } from "@wow-two-beta/fe/presentation/forms";
import { TypedConfirmModal } from "@wow-two-beta/fe/presentation/overlays";

import { useTargetControl } from "@/application/deployments";
import { JobStatus } from "@/domain/deployments";
import { ControlOperation, ServiceControls } from "@/domain/deployments/controls";
import { failureReason } from "@/integration/common";

/**
 * Confirms a stop, start, restart or database reset before it is queued: dev asks once, prod asks for its target ID typed
 * and a reason. The job then reads in the environment's deployments like a deploy; the dialog stays until the target
 * ends it.
 */
defineOptions({ name: "ControlConfirmModal" });
const props = defineProps<ControlConfirmModalProps>();
const emit = defineEmits<{ "update:open": [open: boolean] }>();
defineSlots<{}>();

const control = useTargetControl();
const reason = ref("");
/** While the queued control runs on its target: what it is doing. */
const progress = ref<string | null>(null);
/** Why the target failed or refused the control, once it ended so. */
const outcome = ref<string | null>(null);
const label = computed(() =>
  props.request ? ServiceControls.label(props.request.operation, props.request.service) : "",
);
const title = computed(() => (props.request ? `${label.value} on ${props.request.target.id}?` : ""));
const guarded = computed(() => (props.request ? ServiceControls.isGuarded(props.request.target) : false));
const tone = computed(() =>
  props.request?.operation === ControlOperation.Stop || props.request?.operation === ControlOperation.Reset
    ? "danger"
    : props.request?.operation === ControlOperation.Restart
      ? "warning"
      : "primary",
);
const icon = computed(() =>
  props.request?.operation === ControlOperation.Reset
    ? Eraser
    : props.request?.operation === ControlOperation.Stop
      ? Pause
      : props.request?.operation === ControlOperation.Restart
        ? RotateCw
        : Play,
);
const error = computed(() => outcome.value ?? failureReason(control.error.value));

/** Clears the reason and the last refusal whenever the dialog opens on another control. @internal */
watch(
  () => [props.open, props.request?.target.id, props.request?.operation, props.request?.service],
  () => {
    reason.value = "";
    progress.value = null;
    outcome.value = null;
    control.reset();
  },
);

/** Queues the control — prod sends the typed ID and the reason — and stays until the target ends it: a success closes
 * the dialog, a failure or refusal keeps it open with the target's reason. @internal */
async function submit(typed: string) {
  const request = props.request;
  if (!request) return;
  outcome.value = null;
  const result = await control.mutateAsync({
    target: request.target.id,
    operation: request.operation,
    service: request.service,
    ...(guarded.value ? { reason: reason.value, confirm: typed } : {}),
  });
  if (!result.ok) return result;
  progress.value = `${label.value}…`;
  const ended = await control.follow(result.value.id, request.target.id);
  progress.value = null;
  if (ended && ended.status !== JobStatus.Succeeded) {
    outcome.value = ended.reason || `The target could not ${request.operation} it (${ended.status}).`;
    throw new Error(outcome.value);
  }
  return result;
}
</script>

<template>
  <TypedConfirmModal
    :open="props.open && Boolean(props.request)"
    :title="title"
    :description="props.request ? ServiceControls.consequence(props.request) : ''"
    v-bind="guarded && props.request ? { expected: props.request.target.id } : {}"
    :can-confirm="!guarded || reason.trim().length > 0"
    :confirm-label="props.request ? ServiceControls.verb(props.request.operation) : ''"
    :tone="tone"
    :error="error"
    @update:open="emit('update:open', $event)"
    @confirm="submit"
  >
    <template #confirm-icon><component :is="icon" :size="16" aria-hidden="true" /></template>
    <Field v-if="guarded" label="Reason" is-required>
      <TextInput v-model="reason" autocomplete="off" maxlength="300" />
    </Field>
    <p v-if="progress" role="status" class="text-sm text-muted-foreground">{{ progress }}</p>
  </TypedConfirmModal>
</template>
