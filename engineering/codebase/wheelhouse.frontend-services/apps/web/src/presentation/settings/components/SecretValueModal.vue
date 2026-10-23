<script lang="ts">
import type { EnvironmentKey } from "@/domain/settings";

/** Defines the dialog that writes a secret's next version. */
export interface SecretValueModalProps {
  readonly open: boolean;
  readonly target: string;
  readonly environment: string;
  /** The secret; its value is never shown, only replaced. */
  readonly secret: EnvironmentKey | null;
}
</script>

<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from "vue";
import { Dices } from "lucide-vue-next";
import { Button } from "@wow-two-beta/fe/presentation/actions";
import { Alert } from "@wow-two-beta/fe/presentation/feedback";
import { CheckboxField, Field, TextInput, ToggleGroup, ToggleInput } from "@wow-two-beta/fe/presentation/forms";
import {
  Modal,
  ModalBody,
  ModalContent,
  ModalDescription,
  ModalFooter,
  ModalHeader,
  ModalTitle,
} from "@wow-two-beta/fe/presentation/overlays";
import { useSettingSaver } from "@/application/settings";
import { SettingTypes, type SecretFormat } from "@/domain/settings";
import SettingInput from "./SettingInput.vue";

/**
 * Writes a secret's next version: its value (checked against its type), a note for the history and its expiry, which
 * starts the 30- and 7-day warnings. The value lives only in this dialog and is dropped when it closes. A text secret
 * can be generated instead: Wheelhouse makes 32 random bytes, hex or base64url, straight into the vault, and the
 * browser never sees them.
 */
defineOptions({ name: "SecretValueModal" });
const props = defineProps<SecretValueModalProps>();
const emit = defineEmits<{ "update:open": [open: boolean]; saved: [version: number | null] }>();
defineSlots<{}>();

const saver = useSettingSaver(() => props.target);
const value = ref("");
const note = ref("");
const expires = ref("");
const keepExpiry = ref(true);
const format = ref<SecretFormat>("hex");

/** Drops the value and the form. @internal */
function clear(): void {
  value.value = "";
  note.value = "";
  expires.value = props.secret?.vault?.expiresAt?.slice(0, 10) ?? "";
  keepExpiry.value = true;
  format.value = "hex";
  saver.reset();
}
watch(() => [props.open, props.secret?.key] as const, clear, { immediate: true });
onBeforeUnmount(clear);

const problem = computed(() =>
  value.value && props.secret ? SettingTypes.check(props.secret.type, value.value, props.secret.format, props.secret.allowed) : null,
);
const field = computed(() => (props.secret ? saver.fields[props.secret.key] : undefined));
const canSave = computed(() => Boolean(value.value) && !problem.value && field.value?.state !== "saving");
const canGenerate = computed(() => Boolean(props.secret) && SettingTypes.canGenerate(props.secret?.type));

function setFormat(next: unknown): void {
  if (next === "hex" || next === "base64url") format.value = next;
}

function close(open: boolean): void {
  if (!open) clear();
  emit("update:open", open);
}

/** The note and expiry a new version carries, typed or generated. @internal */
function extras(): { note?: string; expiry?: { at: string | null } } {
  return {
    ...(note.value.trim() ? { note: note.value.trim() } : {}),
    ...(keepExpiry.value ? {} : { expiry: { at: expires.value ? new Date(`${expires.value}T00:00:00Z`).toISOString() : null } }),
  };
}

async function save(event: Event): Promise<void> {
  event.preventDefault();
  if (!props.secret || !canSave.value) return;
  const result = await saver.save([{ key: props.secret.key, value: value.value, secret: true, ...extras() }]);
  if (result.ok) {
    emit("saved", saver.fields[props.secret.key]?.version ?? null);
    close(false);
  }
}

/** Has Wheelhouse generate the next version in the vault; whatever was typed is dropped. @internal */
async function generate(): Promise<void> {
  if (!props.secret || !canGenerate.value || field.value?.state === "saving") return;
  value.value = "";
  const result = await saver.save([{ ...SettingTypes.generated(props.secret.key, format.value), ...extras() }]);
  if (result.ok) {
    emit("saved", saver.fields[props.secret.key]?.version ?? null);
    close(false);
  }
}
</script>

<template>
  <Modal :open="props.open" @update:open="close">
    <ModalContent class="w-full max-w-lg">
      <form @submit="save">
        <ModalHeader>
          <ModalTitle>{{ props.secret?.vault ? `New version of ${props.secret.key}` : `Set ${props.secret?.key}` }}</ModalTitle>
          <ModalDescription>
            {{ props.environment }} keeps every version with who wrote it and a fingerprint. Wheelhouse never shows the
            value again; it reaches the services at the next deploy or apply.
          </ModalDescription>
        </ModalHeader>
        <ModalBody v-if="props.secret" class="flex flex-col gap-4">
          <Field
            label="Value"
            :helper="props.secret.type !== 'text' ? `A ${props.secret.type}${props.secret.format ? ` as ${props.secret.format}` : ''}` : 'Write-only'"
            is-required
          >
            <SettingInput v-model="value" :label="props.secret.key" :type="props.secret.type" secret :format="props.secret.format" />
          </Field>
          <Alert v-if="problem" severity="warning" :description="`${props.secret.key} ${problem}`" />
          <div v-if="canGenerate" class="flex flex-wrap items-center gap-2">
            <Button
              type="button"
              variant="outline"
              tone="neutral"
              size="sm"
              class="max-md:min-h-11"
              :is-disabled="field?.state === 'saving'"
              :aria-label="`Generate ${props.secret.key} in the vault`"
              @click="generate"
              ><template #leading><Dices :size="14" aria-hidden="true" /></template>Generate</Button
            >
            <ToggleGroup variant="segmented" aria-label="Generated format" :model-value="format" @update:model-value="setFormat">
              <ToggleInput v-for="name in SettingTypes.secretFormats" :key="name" :value="name" size="sm" class="max-md:min-h-9">{{
                name
              }}</ToggleInput>
            </ToggleGroup>
            <span class="text-xs text-muted-foreground">32 random bytes, never shown</span>
          </div>
          <Field label="Note" helper="Why it changed, for the history">
            <TextInput v-model="note" autocomplete="off" maxlength="300" />
          </Field>
          <CheckboxField
            v-model="keepExpiry"
            :label="props.secret.vault?.expiresAt ? `Keep its expiry (${props.secret.vault.expiresAt.slice(0, 10)})` : 'No expiry'"
          />
          <Field v-if="!keepExpiry" label="Expires on" helper="Leave empty for none; warnings show 30 and 7 days before">
            <TextInput v-model="expires" type="date" />
          </Field>
          <Alert v-if="field?.state === 'failed'" severity="danger" :description="field.message ?? 'Not saved.'" />
        </ModalBody>
        <ModalFooter>
          <Button type="button" variant="outline" tone="neutral" @click="close(false)">Cancel</Button>
          <Button type="submit" :is-disabled="!canSave" :is-loading="field?.state === 'saving'">Save version</Button>
        </ModalFooter>
      </form>
    </ModalContent>
  </Modal>
</template>
