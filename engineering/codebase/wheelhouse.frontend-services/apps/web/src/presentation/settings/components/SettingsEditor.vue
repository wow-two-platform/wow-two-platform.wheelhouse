<script lang="ts">
import type { DeploySettings } from "@/domain/deployments";

/** Defines an environment's plain settings editor. */
export interface SettingsEditorProps {
  readonly plan: DeploySettings;
  readonly product: string;
}
</script>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { Plus, Sparkles } from "lucide-vue-next";
import { Button } from "@wow-two-beta/fe/presentation/actions";
import { Badge, EmptyState } from "@wow-two-beta/fe/presentation/display";
import { useSettingKeys, useSettingSaver } from "@/application/settings";
import { EnvironmentSettingsRules, type EnvironmentKey } from "@/domain/settings";
import { DeleteConfirm } from "@/presentation/common/components";
import AddKeyModal from "./AddKeyModal.vue";
import SettingField from "./SettingField.vue";

/**
 * The environment's plain settings, every key once: the ones its release declares, with their type and the services
 * that take them, and the ones the owner added. Each field saves as soon as it is confirmed; a set value can be reset
 * to the release's default, and an added key removed. Secrets live on the Secrets tab.
 */
defineOptions({ name: "SettingsEditor" });
const props = defineProps<SettingsEditorProps>();
defineSlots<{}>();

const saver = useSettingSaver(() => props.plan.target);
const keys = useSettingKeys();
const adding = ref(false);
const rows = computed(() => EnvironmentSettingsRules.settings(props.plan));
const derived = computed(() => EnvironmentSettingsRules.derived(props.plan));

watch(() => props.plan.target, () => saver.reset());

function helper(row: EnvironmentKey): string {
  const type = row.type !== "text" ? `${row.type}${row.format ? ` (${row.format})` : ""} · ` : "";
  const who = row.services.length ? row.services.join(", ") : "every service";
  return `${type}${who} · ${EnvironmentSettingsRules.sourceLabel(row)}${row.unapplied ? " · not deployed yet" : ""}`;
}

async function save(row: EnvironmentKey, value: string, confirmPlain: boolean): Promise<void> {
  await saver.save([{ key: row.key, value, secret: false, ...(confirmPlain ? { confirmPlain: true } : {}) }]);
}
</script>

<template>
  <div class="flex flex-col gap-4">
    <div class="flex flex-wrap items-center justify-between gap-3">
      <p class="text-sm text-muted-foreground">Each value saves when you leave its field; it reaches the services at the next deploy or apply.</p>
      <Button size="sm" @click="adding = true"><template #leading><Plus :size="14" /></template>Add setting</Button>
    </div>
    <EmptyState
      v-if="!rows.length"
      size="sm"
      title="No settings to give"
      :description="props.plan.release ? 'The running release declares none beyond what each deploy derives.' : 'Nothing runs here yet; add a setting, or deploy a release to see the ones it declares.'"
    />
    <ul v-else class="flex flex-col divide-y divide-border">
      <li v-for="row in rows" :key="row.key" class="flex flex-col gap-2 py-3 first:pt-0 md:flex-row md:items-start md:gap-4">
        <div class="min-w-0 flex-1">
          <SettingField
            :setting-key="row.key"
            :type="row.type"
            :format="row.format"
            :choices="row.allowed"
            :secret="false"
            :required="row.required"
            :current="row.source === 'stored' ? row.value : null"
            :default-value="row.default"
            :helper="helper(row)"
            :looks-secret="row.looksSecret"
            :save="saver.fields[row.key] ?? null"
            @save="(value: string, confirmPlain: boolean) => save(row, value, confirmPlain)"
          />
        </div>
        <div class="flex shrink-0 flex-wrap items-center gap-2 md:pt-7">
          <Badge v-if="row.source === 'missing'" variant="warning">missing</Badge>
          <Badge v-if="row.unapplied" variant="info">changed</Badge>
          <Badge v-if="row.adHoc" variant="neutral">added</Badge>
          <DeleteConfirm
            v-if="row.source === 'stored'"
            size="sm"
            label="Reset"
            :question="`Remove ${row.key}'s value${row.default !== null ? ' and use the default' : ''}?`"
            :on-confirm="() => saver.save([{ key: row.key, value: '', secret: false, remove: true }])"
          />
          <DeleteConfirm
            v-if="row.adHoc"
            size="sm"
            label="Remove key"
            :question="`Remove the key ${row.key} from ${props.product}? Values already given stay until reset.`"
            :on-confirm="() => keys.remove.mutateAsync({ product: props.product, key: row.key })"
          />
        </div>
      </li>
    </ul>
    <details v-if="derived.length" class="text-sm">
      <summary class="flex cursor-pointer items-center gap-1.5 text-muted-foreground">
        <Sparkles :size="14" aria-hidden="true" />Derived at each deploy ({{ derived.length }})
      </summary>
      <dl class="mt-2 grid grid-cols-[auto_1fr] gap-x-4 gap-y-1.5">
        <template v-for="row in derived" :key="row.key">
          <dt class="font-mono text-xs">{{ row.key }}</dt>
          <dd class="min-w-0 truncate text-xs text-muted-foreground">{{ row.value ?? row.derived }}</dd>
        </template>
      </dl>
    </details>
    <AddKeyModal v-model:open="adding" :product="props.product" :secret="false" />
  </div>
</template>
