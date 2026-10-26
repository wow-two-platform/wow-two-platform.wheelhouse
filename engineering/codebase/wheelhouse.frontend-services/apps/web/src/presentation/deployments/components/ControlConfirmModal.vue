<script lang="ts">
import type { ControlRequest } from "@/domain/deployments/controls";

/** The control the owner asked for and whether its confirmation is open. */
export interface ControlConfirmModalProps {
  /** The stop, start or restart to confirm; null while none is asked for. */
  readonly request: ControlRequest | null;
  /** The controlled dialog state. */
  readonly open: boolean;
}
</script>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { Pause, Play, RotateCw } from "lucide-vue-next";

import { Button } from "@wow-two-beta/fe/presentation/actions";
import { Field, TextInput } from "@wow-two-beta/fe/presentation/forms";
import {
  Modal,
  ModalContent,
  ModalDescription,
  ModalFooter,
  ModalHeader,
  ModalTitle,
  TypedConfirmModal,
} from "@wow-two-beta/fe/presentation/overlays";

import { useTargetControl } from "@/application/deployments";
import { ControlOperation, ServiceControls } from "@/domain/deployments/controls";
import { failureReason } from "@/integration/common";

/**
 * Confirms a stop, start or restart before it is queued: dev asks once, prod asks for its target ID typed and a reason.
 * The job then reads in the environment's deployments like a deploy.
 */
defineOptions({ name: "ControlConfirmModal" });
const props = defineProps<ControlConfirmModalProps>();
const emit = defineEmits<{ "update:open": [open: boolean] }>();
defineSlots<{}>();

const control = useTargetControl();
const reason = ref("");
const missingReason = ref(false);
const label = computed(() =>
  props.request ? ServiceControls.label(props.request.operation, props.request.service) : "",
);
const title = computed(() => (props.request ? `${label.value} on ${props.request.target.id}?` : ""));
const guarded = computed(() => (props.request ? ServiceControls.isGuarded(props.request.target) : false));
const tone = computed(() =>
  props.request?.operation === ControlOperation.Stop
    ? "danger"
    : props.request?.operation === ControlOperation.Restart
      ? "warning"
      : "primary",
);
const error = computed(() => (missingReason.value ? "Give a reason." : failureReason(control.error.value)));

/** Clears the reason and the last refusal whenever the dialog opens on another control. @internal */
watch(
  () => [props.open, props.request?.target.id, props.request?.operation, props.request?.service],
  () => {
    reason.value = "";
    missingReason.value = false;
    control.reset();
  },
);

/** Queues the control; prod sends the typed ID and the reason. @internal */
async function submit(typed?: string) {
  const request = props.request;
  if (!request) return;
  if (guarded.value && !reason.value.trim()) {
    missingReason.value = true;
    throw new Error("Give a reason.");
  }
  missingReason.value = false;
  const result = await control.mutateAsync({
    target: request.target.id,
    operation: request.operation,
    service: request.service,
    ...(guarded.value ? { reason: reason.value, confirm: typed } : {}),
  });
  if (!guarded.value && result.ok) emit("update:open", false);
  return result;
}
</script>

<template>
  <TypedConfirmModal
    v-if="guarded && props.request"
    :open="props.open"
    :title="title"
    :description="ServiceControls.consequence(props.request)"
    :expected="props.request.target.id"
    :confirm-label="ServiceControls.verb(props.request.operation)"
    :tone="tone"
    :error="error"
    @update:open="emit('update:open', $event)"
    @confirm="submit"
  >
    <Field label="Reason" is-required>
      <TextInput v-model="reason" autocomplete="off" maxlength="300" />
    </Field>
  </TypedConfirmModal>
  <Modal v-else :open="props.open && Boolean(props.request)" @update:open="emit('update:open', $event)">
    <ModalContent class="w-full max-w-md">
      <ModalHeader>
        <ModalTitle>{{ title }}</ModalTitle>
        <ModalDescription>{{ props.request ? ServiceControls.consequence(props.request) : "" }}</ModalDescription>
      </ModalHeader>
      <p v-if="error" role="alert" class="px-6 text-sm text-destructive">{{ error }}</p>
      <ModalFooter>
        <Button type="button" variant="outline" tone="neutral" @click="emit('update:open', false)">Cancel</Button>
        <Button type="button" :tone="tone" :is-loading="control.loading.value" @click="submit()">
          <template #leading
            ><Pause v-if="props.request?.operation === ControlOperation.Stop" :size="16" aria-hidden="true" /><RotateCw
              v-else-if="props.request?.operation === ControlOperation.Restart"
              :size="16"
              aria-hidden="true" /><Play v-else :size="16" aria-hidden="true"
          /></template>
          {{ props.request ? ServiceControls.verb(props.request.operation) : "" }}
        </Button>
      </ModalFooter>
    </ModalContent>
  </Modal>
</template>
