<script lang="ts">
import type { DeploySettings } from "@/domain/deployments";

/** Defines the dialog that links an environment's key to another product's key in the same environment. */
export interface LinkKeyModalProps {
  readonly open: boolean;
  readonly plan: DeploySettings;
  /** The key to link; null while closed. */
  readonly settingKey: string | null;
}
</script>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { Button } from "@wow-two-beta/fe/presentation/actions";
import { Alert } from "@wow-two-beta/fe/presentation/feedback";
import {
  ComboboxPicker,
  ComboboxPickerContent,
  ComboboxPickerEmpty,
  ComboboxPickerInput,
  ComboboxPickerItem,
  Field,
  SelectPicker,
  SelectPickerContent,
  SelectPickerItem,
  SelectPickerTrigger,
  SelectPickerValue,
} from "@wow-two-beta/fe/presentation/forms";
import {
  Modal,
  ModalBody,
  ModalContent,
  ModalDescription,
  ModalFooter,
  ModalHeader,
  ModalTitle,
} from "@wow-two-beta/fe/presentation/overlays";
import { useDeploymentTargets } from "@/application/deployments";
import { useEnvironmentSettings, useSettingLinks } from "@/application/settings";
import { EnvironmentSettingsRules } from "@/domain/settings";
import { failureReason } from "@/integration/common";

/**
 * Links a key to another product's key in the same environment, such as a client secret one product's vault generates:
 * pick a target of that product, then type the source key or pick one of the keys it holds. Each deploy renders the
 * source's current value under the key, over a value of its own; neither value ever reaches the browser.
 */
defineOptions({ name: "LinkKeyModal" });
const props = defineProps<LinkKeyModalProps>();
const emit = defineEmits<{ "update:open": [open: boolean] }>();
defineSlots<{}>();

const links = useSettingLinks();
const targets = useDeploymentTargets();
const sourceTarget = ref<string | null>(null);
const sourceKey = ref("");
const choices = computed(() => EnvironmentSettingsRules.linkTargets(targets.data.value, props.plan));
const source = useEnvironmentSettings(() => (props.open ? sourceTarget.value : null));
const suggestions = computed(() => EnvironmentSettingsRules.sourceKeys(source.data.value, sourceKey.value));
const problem = computed(() => (sourceKey.value.trim() ? EnvironmentSettingsRules.linkProblem(sourceTarget.value, sourceKey.value) : null));
const canSave = computed(
  () => Boolean(props.settingKey && sourceTarget.value && sourceKey.value.trim()) && !problem.value && !links.link.loading.value,
);

watch(
  () => props.open,
  (open) => {
    if (!open) return;
    sourceTarget.value = choices.value.length === 1 ? choices.value[0]! : null;
    sourceKey.value = "";
    links.link.reset();
  },
);

async function save(event: Event): Promise<void> {
  event.preventDefault();
  if (!canSave.value || !props.settingKey || !sourceTarget.value) return;
  const result = await links.link.mutateAsync({
    target: props.plan.target,
    key: props.settingKey,
    source: { target: sourceTarget.value, key: sourceKey.value.trim() },
  });
  if (result.ok) emit("update:open", false);
}
</script>

<template>
  <Modal :open="props.open" @update:open="(open: boolean) => emit('update:open', open)">
    <ModalContent class="w-full max-w-lg">
      <form @submit="save">
        <ModalHeader>
          <ModalTitle>Link {{ props.settingKey }}</ModalTitle>
          <ModalDescription>
            Each deploy of {{ props.plan.environment }} renders another product's key here, over a value of its own. The
            value never shows; a new version of the source reaches this key at the next deploy or apply.
          </ModalDescription>
        </ModalHeader>
        <ModalBody class="flex flex-col gap-4">
          <Field label="Source target" :helper="`A target of another product in ${props.plan.environment}`" is-required>
            <SelectPicker :model-value="sourceTarget" @update:model-value="(value: string | null) => (sourceTarget = value)">
              <SelectPickerTrigger aria-label="Source target"><SelectPickerValue placeholder="Pick a target" /></SelectPickerTrigger>
              <SelectPickerContent>
                <SelectPickerItem v-for="slug in choices" :key="slug" :item-key="slug" :label="slug" />
              </SelectPickerContent>
            </SelectPicker>
          </Field>
          <Field label="Source key" helper="Type the configuration path, or pick one the source holds" is-required>
            <ComboboxPicker v-model:input-value="sourceKey" :is-disabled="!sourceTarget">
              <ComboboxPickerInput aria-label="Source key" autocomplete="off" spellcheck="false" />
              <ComboboxPickerContent>
                <ComboboxPickerItem v-for="key in suggestions" :key="key.key" :value="key.key" :label="key.key" />
                <ComboboxPickerEmpty>{{ source.data.value ? "No key of the source matches" : "Pick a target first" }}</ComboboxPickerEmpty>
              </ComboboxPickerContent>
            </ComboboxPicker>
          </Field>
          <Alert v-if="!choices.length" severity="info" :description="`No other product has a ${props.plan.environment} target to link to.`" />
          <Alert v-if="problem" severity="warning" :description="problem" />
          <Alert v-if="links.link.error.value" severity="danger" :description="failureReason(links.link.error.value)" />
        </ModalBody>
        <ModalFooter>
          <Button type="button" variant="outline" tone="neutral" @click="emit('update:open', false)">Cancel</Button>
          <Button type="submit" :is-disabled="!canSave" :is-loading="links.link.loading.value">Link key</Button>
        </ModalFooter>
      </form>
    </ModalContent>
  </Modal>
</template>
