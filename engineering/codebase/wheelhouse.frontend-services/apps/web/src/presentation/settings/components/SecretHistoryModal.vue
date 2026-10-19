<script lang="ts">
/** Defines the dialog that shows a secret's versions and restores one. */
export interface SecretHistoryModalProps {
  readonly open: boolean;
  readonly target: string;
  /** The secret's key; null while closed. */
  readonly secretKey: string | null;
}
</script>

<script setup lang="ts">
import { ref, watch } from "vue";
import { RotateCcw } from "lucide-vue-next";
import { Button } from "@wow-two-beta/fe/presentation/actions";
import { Badge, RelativeTime } from "@wow-two-beta/fe/presentation/display";
import { Alert, DataRegion } from "@wow-two-beta/fe/presentation/feedback";
import { Field, TextInput } from "@wow-two-beta/fe/presentation/forms";
import {
  Modal,
  ModalBody,
  ModalContent,
  ModalDescription,
  ModalFooter,
  ModalHeader,
  ModalTitle,
} from "@wow-two-beta/fe/presentation/overlays";
import { useRestoreSecret, useSecretExpiry, useSecretHistory } from "@/application/settings";
import { failureReason } from "@/integration/common";

/**
 * A secret's history, newest first: each version's author, time, fingerprint (equal values show equal ones), note and
 * origin — never a value. An earlier version restores as a new one; the expiry can be set or cleared here too.
 */
defineOptions({ name: "SecretHistoryModal" });
const props = defineProps<SecretHistoryModalProps>();
const emit = defineEmits<{ "update:open": [open: boolean] }>();
defineSlots<{}>();

const history = useSecretHistory(
  () => (props.open ? props.target : null),
  () => (props.open ? props.secretKey : null),
);
const restore = useRestoreSecret();
const expiry = useSecretExpiry();
const note = ref("");
const expires = ref("");

watch(
  () => history.data.value?.expiresAt,
  (at) => {
    expires.value = at?.slice(0, 10) ?? "";
  },
  { immediate: true },
);
watch(
  () => props.open,
  () => {
    note.value = "";
    restore.reset();
    expiry.reset();
  },
);

async function restoreVersion(version: number): Promise<void> {
  if (!props.secretKey) return;
  await restore.mutateAsync({ target: props.target, key: props.secretKey, version, note: note.value.trim() || null });
  void history.refetch();
}

async function saveExpiry(): Promise<void> {
  if (!props.secretKey) return;
  await expiry.mutateAsync({
    target: props.target,
    key: props.secretKey,
    expiresAt: expires.value ? new Date(`${expires.value}T00:00:00Z`).toISOString() : null,
  });
  void history.refetch();
}
</script>

<template>
  <Modal :open="props.open" @update:open="(open: boolean) => emit('update:open', open)">
    <ModalContent class="w-full max-w-2xl">
      <ModalHeader>
        <ModalTitle>History of {{ props.secretKey }}</ModalTitle>
        <ModalDescription>Every version this environment kept. Values never leave the vault.</ModalDescription>
      </ModalHeader>
      <ModalBody class="flex flex-col gap-4">
        <DataRegion
          :is-first-load="history.isFirstLoad.value"
          :error="history.error.value"
          :has-data="Boolean(history.data.value)"
          loading-label="Reading the history"
        >
          <Badge v-if="history.data.value?.removed" variant="warning" class="self-start">removed: renders nowhere</Badge>
          <ol class="flex flex-col divide-y divide-border">
            <li
              v-for="version in history.data.value?.versions ?? []"
              :key="version.version"
              class="flex flex-wrap items-center justify-between gap-3 py-2.5"
            >
              <div class="min-w-0">
                <p class="flex flex-wrap items-center gap-2 text-sm">
                  <span class="font-medium">v{{ version.version }}</span>
                  <Badge v-if="version.current" variant="success">current</Badge>
                  <span class="font-mono text-xs text-muted-foreground" title="Fingerprint">{{ version.fingerprint }}</span>
                </p>
                <p class="text-xs text-muted-foreground">
                  {{ version.createdBy }} · <RelativeTime :value="version.createdAt" /><template v-if="version.origin">
                    · {{ version.origin }}</template
                  ><template v-if="version.note"> · “{{ version.note }}”</template>
                </p>
              </div>
              <Button
                v-if="!version.current"
                size="sm"
                variant="outline"
                tone="neutral"
                :is-loading="restore.loading.value"
                :aria-label="`Restore version ${version.version} of ${props.secretKey}`"
                @click="restoreVersion(version.version)"
              >
                <template #leading><RotateCcw :size="14" aria-hidden="true" /></template>Restore
              </Button>
            </li>
          </ol>
        </DataRegion>
        <Field label="Note for a restore" helper="Kept with the new version">
          <TextInput v-model="note" autocomplete="off" maxlength="300" />
        </Field>
        <div class="flex flex-wrap items-end gap-2">
          <Field label="Expires on" helper="Empty clears it; warnings show 30 and 7 days before" class="min-w-0 flex-1">
            <TextInput v-model="expires" type="date" />
          </Field>
          <Button variant="outline" tone="neutral" :is-loading="expiry.loading.value" @click="saveExpiry">Save expiry</Button>
        </div>
        <Alert
          v-if="restore.error.value || expiry.error.value"
          severity="danger"
          :description="failureReason(restore.error.value ?? expiry.error.value)"
        />
      </ModalBody>
      <ModalFooter>
        <Button variant="outline" tone="neutral" @click="emit('update:open', false)">Close</Button>
      </ModalFooter>
    </ModalContent>
  </Modal>
</template>
