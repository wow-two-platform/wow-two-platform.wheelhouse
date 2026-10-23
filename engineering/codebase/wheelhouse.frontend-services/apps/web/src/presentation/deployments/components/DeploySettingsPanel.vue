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
import { computed, nextTick, ref, watch } from "vue";
import { Check, KeyRound, Pencil, Sparkles } from "lucide-vue-next";

import { Button } from "@wow-two-beta/fe/presentation/actions";
import { Badge } from "@wow-two-beta/fe/presentation/display";
import { Alert } from "@wow-two-beta/fe/presentation/feedback";

import { useSettingSaver } from "@/application/settings";
import { DeploySettingsRules } from "@/domain/deployments";
import { SettingTypes } from "@/domain/settings";

import DeploySettingField from "./DeploySettingField.vue";

/**
 * Lists the required values a deploy still lacks first, then the values already set — key, service and value, a
 * secret shown as set in the vault, each editable on click — then the optional keys of a first deploy, closed, and what
 * the render derives on its own. Each field saves as soon as it is confirmed — secrets to the vault as a new version,
 * the rest to the store — and shows saving, saved or why it failed, so what the owner gave survives closing the dialog;
 * a required key without a saved value still holds the deploy. A text secret can be generated on the server instead.
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
/** The set value being edited in place, by key. */
const editing = ref<string | null>(null);
/* Another target or release starts over. */
watch(
  () => [props.target, props.release] as const,
  () => {
    shown.value = [...props.prompted];
    editing.value = null;
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
const sections = computed(() => DeploySettingsRules.sections(props.settings, shown.value, entered.value, secrets.value));
const missing = computed(() => sections.value.missing);
const held = computed(() => sections.value.held);
const optional = computed(() => sections.value.optional);
const hostFiles = computed(() => (props.settings?.services ?? []).filter((service) => service.hostFile).map((service) => service.name));
const sealed = computed(() => props.settings?.vault?.sealed ?? null);

function secretOf(item: PromptedSetting): boolean {
  return secrets.value[item.key] ?? item.secret;
}

function setSecret(key: string, value: boolean): void {
  secrets.value = { ...secrets.value, [key]: value };
}

/** Saves one field; once saved, it counts as given, and an edited value closes. @internal */
async function save(item: PromptedSetting, value: string, confirmPlain: boolean): Promise<void> {
  const secret = secretOf(item);
  const result = await saver.save([{ key: item.key, value, secret, ...(confirmPlain ? { confirmPlain: true } : {}) }]);
  if (!result.ok) return;
  entered.value = { ...entered.value, [item.key]: secret ? "saved" : value };
  if (editing.value === item.key) editing.value = null;
}

/** Has Wheelhouse generate a secret's value into the vault; once saved, it counts as given. @internal */
async function generate(item: PromptedSetting): Promise<void> {
  const result = await saver.save([SettingTypes.generated(item.key)]);
  if (!result.ok) return;
  entered.value = { ...entered.value, [item.key]: "saved" };
  if (editing.value === item.key) editing.value = null;
}

/** Opens a set value for editing and puts the cursor in its field. @internal */
function edit(key: string, event: MouseEvent): void {
  editing.value = key;
  const row = (event.currentTarget as HTMLElement).closest("li");
  void nextTick(() => row?.querySelector<HTMLElement>("input, textarea, select, button")?.focus());
}
</script>

<template>
  <section class="flex flex-col gap-3 rounded-xl border border-border p-4" aria-label="Settings">
    <div class="flex flex-wrap items-center gap-2">
      <p class="text-sm font-medium">Settings</p>
      <Badge v-if="missing.length" variant="warning"
        >{{ missing.length }} required {{ missing.length === 1 ? "value" : "values" }} missing{{ held.length ? ` · ${held.length} set` : "" }}</Badge
      >
      <template v-else>
        <Badge v-if="held.length" variant="success">{{ held.length }} set</Badge>
        <Badge v-if="props.settings?.firstDeploy" variant="info">First deploy</Badge>
      </template>
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
    <template v-if="missing.length">
      <p class="text-xs text-muted-foreground">
        Each value saves when you leave its field or press Enter, so it stays even if you close this dialog.
      </p>
      <div class="flex flex-col gap-3">
        <DeploySettingField
          v-for="item in missing"
          :key="item.key"
          :item="item"
          :secret="secretOf(item)"
          :save="saver.fields[item.key] ?? null"
          :can-choose-vault="!item.declaredSecret"
          :is-disabled="props.isDisabled"
          @save="(value: string, confirmPlain: boolean) => save(item, value, confirmPlain)"
          @generate="generate(item)"
          @update:secret="(value: boolean) => setSecret(item.key, value)"
        />
      </div>
    </template>
    <p v-else class="text-sm text-muted-foreground">
      Every required value is set.{{ held.length ? " Click a value to change it." : "" }} Values save to this environment: secrets to its vault, the rest to Wheelhouse.
    </p>
    <div v-if="held.length" class="flex flex-col gap-1.5">
      <p class="text-xs font-medium text-muted-foreground">Set ({{ held.length }})</p>
      <ul class="flex flex-col divide-y divide-border rounded-lg border border-border" aria-label="Values already set">
        <li v-for="item in held" :key="item.key">
          <div v-if="editing === item.key" class="flex flex-col gap-1.5 px-3 py-2">
            <DeploySettingField
              :item="item"
              :secret="secretOf(item)"
              :current="item.secret ? null : item.value"
              :save="saver.fields[item.key] ?? null"
              :is-disabled="props.isDisabled"
              @save="(value: string, confirmPlain: boolean) => save(item, value, confirmPlain)"
          @generate="generate(item)"
            />
            <div class="flex justify-end">
              <Button type="button" size="sm" variant="outline" tone="neutral" @click="editing = null">Done</Button>
            </div>
          </div>
          <button
            v-else
            type="button"
            class="group grid w-full grid-cols-[minmax(0,1.2fr)_minmax(0,1fr)_1rem] items-center gap-x-3 px-3 py-1.5 text-left text-xs hover:bg-muted/50 focus-visible:outline-2 focus-visible:outline-ring disabled:opacity-60"
            :disabled="props.isDisabled"
            :aria-label="`Edit ${item.key}: ${item.secret ? 'set in the vault' : item.value || 'empty'}`"
            @click="edit(item.key, $event)"
          >
            <span class="flex min-w-0 flex-col">
              <span class="font-mono [overflow-wrap:anywhere]">{{ item.key }}</span>
              <span class="truncate text-muted-foreground" :title="item.services.join(', ')">{{ item.services.join(", ") }}</span>
            </span>
            <span v-if="item.secret" class="truncate italic text-muted-foreground">set in the vault</span>
            <span v-else-if="item.value" class="truncate font-mono" :title="item.value">{{ item.value }}</span>
            <span v-else class="truncate italic text-muted-foreground">empty</span>
            <span class="flex items-center justify-end" aria-hidden="true">
              <Check v-if="saver.fields[item.key]?.state === 'saved'" :size="14" class="text-success" />
              <Pencil v-else :size="12" class="text-muted-foreground opacity-0 group-hover:opacity-100 group-focus-visible:opacity-100" />
            </span>
          </button>
        </li>
      </ul>
    </div>
    <details v-if="optional.length" class="text-sm">
      <summary class="cursor-pointer text-muted-foreground">Optional ({{ optional.length }})</summary>
      <div class="mt-3 flex flex-col gap-3">
        <DeploySettingField
          v-for="item in optional"
          :key="item.key"
          :item="item"
          :secret="secretOf(item)"
          :save="saver.fields[item.key] ?? null"
          :can-choose-vault="!item.declaredSecret"
          :is-disabled="props.isDisabled"
          @save="(value: string, confirmPlain: boolean) => save(item, value, confirmPlain)"
          @generate="generate(item)"
          @update:secret="(value: boolean) => setSecret(item.key, value)"
        />
      </div>
    </details>
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
