<script lang="ts">
/** Defines which service's recent container output the viewer reads. */
export interface ServiceLogsModalProps {
  /** Whether the viewer is open. */
  readonly open: boolean;
  /** The target the service runs on. */
  readonly target: string | null;
  /** The service whose container output to read. */
  readonly service: string | null;
}
</script>

<script setup lang="ts">
import { computed, nextTick, ref, watch } from "vue";

import { Button } from "@wow-two-beta/ui-vue/presentation/actions";
import { Alert, Spinner } from "@wow-two-beta/ui-vue/presentation/feedback";
import {
  SelectPicker,
  SelectPickerContent,
  SelectPickerItem,
  SelectPickerTrigger,
  SelectPickerValue,
} from "@wow-two-beta/ui-vue/presentation/forms";
import {
  Modal,
  ModalBody,
  ModalContent,
  ModalDescription,
  ModalFooter,
  ModalHeader,
  ModalTitle,
} from "@wow-two-beta/ui-vue/presentation/overlays";

import { useRefresh } from "@/bootstrap/query";
import { useServiceLogs } from "@/application/deployments";
import { Measures } from "@/domain/common";
import { RefreshButton } from "@/presentation/common/components";

/** Reads one service's last container lines on demand. Nothing stores them; closing the viewer discards the view. */
defineOptions({ name: "ServiceLogsModal" });
const props = defineProps<ServiceLogsModalProps>();
const emit = defineEmits<{ "update:open": [open: boolean] }>();

const TailOptions = ["100", "200", "500", "1000"] as const;
const tail = ref<string>("200");
const logs = useServiceLogs(
  () => (props.open ? props.target : null),
  () => (props.open ? props.service : null),
  () => Number(tail.value),
);
const refresh = useRefresh(() => logs.refetch());
const output = ref<HTMLElement | null>(null);
const lines = computed(() => logs.data.value?.lines ?? []);

/** Reads fresh output on every opening and keeps the newest line in view. */
watch(
  () => props.open,
  (open) => {
    if (open && logs.data.value) void logs.refetch();
  },
);
watch(lines, async () => {
  await nextTick();
  output.value?.scrollTo({ top: output.value.scrollHeight });
});
</script>

<template>
  <Modal :open="props.open" @update:open="emit('update:open', $event)">
    <ModalContent class="flex max-h-[calc(100dvh-2rem)] flex-col w-full max-w-4xl">
      <ModalHeader>
        <ModalTitle>Logs · {{ props.service }}</ModalTitle>
        <ModalDescription
          >The last lines the service's container wrote on
          <span class="font-mono">{{ props.target }}</span
          >. Read on request; Wheelhouse never stores them.</ModalDescription
        >
      </ModalHeader>
      <ModalBody class="-mx-1 min-h-0 flex-1 overflow-y-auto px-1 flex flex-col gap-3">
        <div class="flex flex-wrap items-center justify-between gap-3">
          <p class="text-xs text-muted-foreground" role="status">
            {{
              logs.data.value
                ? `${lines.length} lines · read ${Measures.moment(logs.data.value.collectedAt)}` +
                  (logs.data.value.truncated ? " · long lines shortened" : "")
                : "Reading the container output…"
            }}
          </p>
          <div class="flex items-center gap-2">
            <div class="w-32">
              <SelectPicker v-model="tail"
                ><SelectPickerTrigger aria-label="Lines to read"
                  ><SelectPickerValue /></SelectPickerTrigger
                ><SelectPickerContent
                  ><SelectPickerItem
                    v-for="option in TailOptions"
                    :key="option"
                    :item-key="option"
                    :value="option"
                    :label="`${option} lines`"
                  /></SelectPickerContent
                ></SelectPicker
              >
            </div>
            <RefreshButton :refreshing="refresh.refreshing.value" @refresh="refresh.refresh" />
          </div>
        </div>
        <Alert
          v-if="logs.error.value"
          severity="warning"
          title="Logs unavailable"
          :description="logs.error.value.message"
        />
        <div v-else-if="logs.loading.value && !logs.data.value" class="flex justify-center py-10">
          <Spinner label="Reading the container output" />
        </div>
        <pre
          v-else
          ref="output"
          tabindex="0"
          aria-label="Container output"
          class="max-h-[60vh] min-h-48 overflow-auto rounded-xl border border-border bg-muted/40 p-3 font-mono text-xs leading-relaxed whitespace-pre-wrap break-all"
          >{{ lines.length ? lines.join("\n") : "The container wrote no output." }}</pre
        >
      </ModalBody>
      <ModalFooter
        ><Button variant="outline" tone="neutral" @click="emit('update:open', false)"
          >Close</Button
        ></ModalFooter
      >
    </ModalContent>
  </Modal>
</template>
