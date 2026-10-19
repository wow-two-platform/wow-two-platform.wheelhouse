<script lang="ts">
import type { DeploySettings, PromptedSetting } from "@/domain/deployments";

/** Defines the deploy dialog's settings step: what the release takes and what the owner must still give. */
export interface DeploySettingsPanelProps {
  /** The settings plan of the chosen release on the chosen target; null while it loads. */
  readonly settings: DeploySettings | null;
  /** The keys asked for: required ones no layer sets, and on a first deploy the optional ones. */
  readonly prompted: readonly PromptedSetting[];
  /** The target each field saves to. */
  readonly target: string;
  /** The release being deployed, whose declarations type the values. */
  readonly release: string | null;
  /** Whether the controls take input. */
  readonly isDisabled?: boolean;
}
</script>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { KeyRound, Sparkles } from "lucide-vue-next";

import { Badge } from "@wow-two-beta/fe/presentation/display";
import { Alert } from "@wow-two-beta/fe/presentation/feedback";
import { CheckboxField } from "@wow-two-beta/fe/presentation/forms";

import { useSettingSaver } from "@/application/settings";
import { DeploySettingsRules } from "@/domain/deployments";
import SettingField from "@/presentation/settings/components/SettingField.vue";

/**
 * Lists the values a deploy still needs, then what the render derives on its own. Each field saves as soon as it is
 * confirmed — secrets to the vault as a new version, the rest to the store — and shows saving, saved or why it failed,
 * so what the owner gave survives closing the dialog; a required key without a saved value still holds the deploy.
 * Secrets never come back to the browser.
 */
defineOptions({ name: "DeploySettingsPanel" });
const props = withDefaults(defineProps<DeploySettingsPanelProps>(), { isDisabled: false });
/** What each key holds once saved: a setting's value, or a marker for a secret's; a blank required one blocks. */
const entered = defineModel<Record<string, string>>("entered", { required: true });
/** Whether each entry goes to the vault, by key; defaults to the release's declaration. */
const secrets = defineModel<Record<string, boolean>>("secrets", { required: true });
/** Whether a field's save is still on its way: the deploy waits for it. */
const saving = defineModel<boolean>("saving", { default: false });

const saver = useSettingSaver(
  () => props.target,
  () => props.release,
);
/** The keys asked for this session: a saved one stays, marked saved, after the plan reads again without it. */
const shown = ref<PromptedSetting[]>([]);
watch(
  () => props.prompted,
  (prompted) => {
    const known = new Map(shown.value.map((item) => [item.key, item]));
    for (const item of prompted) known.set(item.key, item);
    shown.value = [...known.values()].sort(
      (a, b) => Number(b.required) - Number(a.required) || a.key.localeCompare(b.key),
    );
  },
  { immediate: true },
);
/* Another target or release starts over. */
watch(
  () => [props.target, props.release] as const,
  () => {
    shown.value = [...props.prompted];
    saver.reset();
  },
);
watch(
  () => Object.values(saver.fields).some((field) => field.state === "saving"),
  (busy) => {
    saving.value = busy;
  },
);

const derived = computed(() => DeploySettingsRules.derived(props.settings));
const blocking = computed(() => DeploySettingsRules.blocking(shown.value, entered.value));
const hostFiles = computed(() => (props.settings?.services ?? []).filter((service) => service.hostFile).map((service) => service.name));
const sealed = computed(() => props.settings?.vault?.sealed ?? null);

function secretOf(item: PromptedSetting): boolean {
  return secrets.value[item.key] ?? item.secret;
}

function setSecret(key: string, value: boolean): void {
  secrets.value = { ...secrets.value, [key]: value };
}

/** Saves one field; once saved, it counts as given. @internal */
async function save(item: PromptedSetting, value: string, confirmPlain: boolean): Promise<void> {
  const secret = secretOf(item);
  const result = await saver.save([{ key: item.key, value, secret, ...(confirmPlain ? { confirmPlain: true } : {}) }]);
  if (result.ok) entered.value = { ...entered.value, [item.key]: secret ? "saved" : value };
}

function helper(item: PromptedSetting): string {
  const where = item.services.join(", ");
  const kind = item.type && item.type !== "text" ? `${item.type}${item.format ? ` (${item.format})` : ""}; ` : "";
  if (item.required) return `${kind}Required by ${where}`;
  return item.default !== null ? `${kind}Optional for ${where}; default ${item.default || "empty"}` : `${kind}Optional for ${where}`;
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
      v-if="sealed"
      severity="warning"
      title="The vault is sealed"
      :description="sealed === 'mismatch'
        ? 'Its root key file belongs to another vault. Secrets neither save nor render until the right key is back.'
        : 'Its root key file is missing or unreadable. Secrets neither save nor render until it is placed.'"
    />
    <Alert
      v-else-if="props.settings?.vault && !props.settings.vault.reachable"
      severity="warning"
      :description="`The vault ${props.settings.vault.id} is unreachable; the deploy waits until it answers, so no secret is dropped.`"
    />
    <p v-if="shown.length" class="text-xs text-muted-foreground">
      Each value saves when you leave its field or press Enter, so it stays even if you close this dialog.
    </p>
    <div v-if="shown.length" class="flex flex-col gap-3">
      <div v-for="item in shown" :key="item.key" class="flex flex-col gap-1.5">
        <SettingField
          :setting-key="item.key"
          :type="item.type ?? 'text'"
          :format="item.format ?? null"
          :choices="item.allowed"
          :secret="secretOf(item)"
          :required="item.required"
          :default-value="item.default"
          :helper="helper(item)"
          :looks-secret="item.looksSecret ?? false"
          :save="saver.fields[item.key] ?? null"
          :is-disabled="props.isDisabled"
          @save="(value: string, confirmPlain: boolean) => save(item, value, confirmPlain)"
        />
        <CheckboxField
          v-if="!item.declaredSecret"
          :model-value="secretOf(item)"
          :disabled="props.isDisabled || saver.fields[item.key]?.state === 'saved'"
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
