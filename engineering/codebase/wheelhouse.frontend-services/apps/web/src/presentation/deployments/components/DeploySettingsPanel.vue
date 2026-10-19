<script lang="ts">
import type { DeploySettings, PromptedSetting } from "@/domain/deployments";

/** Defines the deploy dialog's settings step: what the release takes and what the owner must still give. */
export interface DeploySettingsPanelProps {
  /** The settings plan of the chosen release on the chosen target; null while it loads. */
  readonly settings: DeploySettings | null;
  /** The keys asked for: required ones no layer sets, and on a first deploy the optional ones. */
  readonly prompted: readonly PromptedSetting[];
  /** Whether the controls take input. */
  readonly isDisabled?: boolean;
}
</script>

<script setup lang="ts">
import { computed } from "vue";
import { KeyRound, Sparkles } from "lucide-vue-next";

import { Badge } from "@wow-two-beta/fe/presentation/display";
import { Alert } from "@wow-two-beta/fe/presentation/feedback";
import {
  CheckboxField,
  Field,
  PasswordInput,
  SelectPicker,
  SelectPickerContent,
  SelectPickerItem,
  SelectPickerTrigger,
  SelectPickerValue,
  TextInput,
} from "@wow-two-beta/fe/presentation/forms";

import { DeploySettingsRules } from "@/domain/deployments";

/** Lists the values a deploy still needs, then what the render derives on its own. Secrets go to the vault once
 *  saved and never come back to the browser. */
defineOptions({ name: "DeploySettingsPanel" });
const props = withDefaults(defineProps<DeploySettingsPanelProps>(), { isDisabled: false });
/** The owner's entries by key; a blank optional one keeps its default. */
const entered = defineModel<Record<string, string>>("entered", { required: true });
/** Whether each entry goes to the vault, by key; defaults to the release's declaration. */
const secrets = defineModel<Record<string, boolean>>("secrets", { required: true });

const derived = computed(() => DeploySettingsRules.derived(props.settings));
const blocking = computed(() => DeploySettingsRules.blocking(props.prompted, entered.value));
const hostFiles = computed(() => (props.settings?.services ?? []).filter((service) => service.hostFile).map((service) => service.name));

function set(key: string, value: string | null): void {
  entered.value = { ...entered.value, [key]: value ?? "" };
}

function secretOf(item: PromptedSetting): boolean {
  return secrets.value[item.key] ?? item.secret;
}

function setSecret(key: string, value: boolean): void {
  secrets.value = { ...secrets.value, [key]: value };
}

function helper(item: PromptedSetting): string {
  const where = item.services.join(", ");
  if (item.required) return `Required by ${where}`;
  return item.default !== null ? `Optional for ${where}; default ${item.default || "empty"}` : `Optional for ${where}`;
}
</script>

<template>
  <section class="flex flex-col gap-3 rounded-xl border border-border p-4" aria-label="Settings">
    <div class="flex flex-wrap items-center gap-2">
      <p class="text-sm font-medium">Settings</p>
      <Badge v-if="blocking.length" variant="warning">{{ blocking.length }} required {{ blocking.length === 1 ? "value" : "values" }} missing</Badge>
      <Badge v-else-if="props.settings?.firstDeploy" variant="info">First deploy</Badge>
    </div>
    <Alert
      v-if="props.settings?.vault && !props.settings.vault.reachable"
      severity="warning"
      :description="`The vault ${props.settings.vault.id} is unreachable; the deploy waits until it answers, so no secret is dropped.`"
    />
    <Alert
      v-if="props.prompted.some((item) => secretOf(item)) && !props.settings?.vault"
      severity="warning"
      description="No vault holds this environment's secrets yet; add one before saving a secret."
    />
    <div v-if="props.prompted.length" class="flex flex-col gap-3">
      <div v-for="item in props.prompted" :key="item.key" class="flex flex-col gap-1.5">
        <Field :label="item.key" :helper="helper(item)" :is-required="item.required">
          <SelectPicker
            v-if="item.allowed?.length"
            :model-value="entered[item.key] || null"
            :is-disabled="props.isDisabled"
            @update:model-value="(value: string | null) => set(item.key, value)"
          >
            <SelectPickerTrigger :aria-label="item.key">
              <SelectPickerValue :placeholder="item.default ?? 'Choose a value'" />
            </SelectPickerTrigger>
            <SelectPickerContent>
              <SelectPickerItem v-for="choice in item.allowed" :key="choice" :item-key="choice" :label="choice" />
            </SelectPickerContent>
          </SelectPicker>
          <PasswordInput
            v-else-if="secretOf(item)"
            :model-value="entered[item.key] ?? ''"
            :is-disabled="props.isDisabled"
            autocomplete="new-password"
            @update:model-value="(value: string) => set(item.key, value)"
          />
          <TextInput
            v-else
            :model-value="entered[item.key] ?? ''"
            :is-disabled="props.isDisabled"
            :placeholder="item.default ?? ''"
            autocomplete="off"
            @update:model-value="(value: string) => set(item.key, value)"
          />
        </Field>
        <CheckboxField
          :model-value="secretOf(item)"
          :disabled="props.isDisabled"
          label="Secret: keep it in the vault"
          @update:model-value="(value: boolean) => setSecret(item.key, value)"
        />
      </div>
    </div>
    <p v-else class="text-sm text-muted-foreground">
      Every required value is set. Values save to this environment: secrets to its vault, the rest to Wheelhouse.
    </p>
    <p v-if="hostFiles.length" class="text-xs text-muted-foreground">
      Host files of {{ hostFiles.join(", ") }} stay a layer: Wheelhouse's values and the derived keys go over them.
    </p>
    <details v-if="derived.length" class="text-sm">
      <summary class="flex cursor-pointer items-center gap-1.5 text-muted-foreground">
        <Sparkles :size="14" aria-hidden="true" />Derived at each deploy ({{ derived.length }})
      </summary>
      <dl class="mt-2 grid grid-cols-[auto_1fr] gap-x-4 gap-y-1.5">
        <template v-for="item in derived" :key="item.key">
          <dt class="flex items-center gap-1 font-mono text-xs">
            <KeyRound v-if="item.derived === 'database'" :size="12" aria-hidden="true" />{{ item.key }}
          </dt>
          <dd class="min-w-0 truncate text-xs text-muted-foreground" :title="DeploySettingsRules.describe(item)">
            {{ DeploySettingsRules.describe(item) }}
          </dd>
        </template>
      </dl>
    </details>
  </section>
</template>
