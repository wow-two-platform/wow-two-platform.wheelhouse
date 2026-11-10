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
import { Link2, Plus, Sparkles } from "lucide-vue-next";
import { Button } from "@wow-two-beta/fe/presentation/actions";
import { Badge, EmptyState } from "@wow-two-beta/fe/presentation/display";
import { useSettingKeys, useSettingLinks, useSettingSaver } from "@/application/settings";
import { EnvironmentSettingsRules, type EnvironmentKey } from "@/domain/settings";
import { DeleteConfirm } from "@/presentation/common/components";
import AddKeyModal from "./AddKeyModal.vue";
import LinkKeyModal from "./LinkKeyModal.vue";
import SettingField from "./SettingField.vue";

/**
 * The environment's plain settings, every key once: the ones its release declares, with their type and the services
 * that take them, and the ones the owner added. Each field saves as soon as it is confirmed; a set value can be reset
 * to the release's default, and an added key removed. A linked key shows its source and the source's value, read-only:
 * the source is the one to change. Link takes a key's value from another product's key in the same environment, and
 * Unlink gives it back its own. Secrets live on the Secrets tab.
 */
defineOptions({ name: "SettingsEditor" });
const props = defineProps<SettingsEditorProps>();
defineSlots<{}>();

const saver = useSettingSaver(() => props.plan.target);
const keys = useSettingKeys();
const links = useSettingLinks();
const adding = ref(false);
const linking = ref<string | null>(null);
const rows = computed(() => EnvironmentSettingsRules.settings(props.plan));
const derived = computed(() => EnvironmentSettingsRules.derived(props.plan));

watch(() => props.plan.target, () => saver.reset());

function helper(row: EnvironmentKey): string {
  const type = row.type !== "text" ? `${row.type}${row.format ? ` (${row.format})` : ""} · ` : "";
  const who = EnvironmentSettingsRules.takers(row);
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
        <div v-if="EnvironmentSettingsRules.isLinked(row)" class="flex min-w-0 flex-1 flex-col gap-1" :aria-label="`${row.key}, linked`">
          <span class="font-mono text-xs [overflow-wrap:anywhere]">{{ row.key }}</span>
          <span class="flex items-center gap-1 text-xs text-muted-foreground">
            <Link2 :size="12" aria-hidden="true" />{{ helper(row) }}
          </span>
          <span v-if="row.value !== null" class="truncate font-mono text-sm" :title="row.value">{{ row.value || "empty" }}</span>
          <span v-else class="text-sm italic text-muted-foreground">the source's value, at each deploy</span>
        </div>
        <div v-else class="min-w-0 flex-1">
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
          <Badge v-if="EnvironmentSettingsRules.isLinked(row)" variant="neutral">linked</Badge>
          <Badge v-if="row.unapplied" variant="info">changed</Badge>
          <Badge v-if="row.adHoc" variant="neutral">added</Badge>
          <Badge v-if="row.held" variant="info" title="No service of this release declares it; it waits for a release that does">held</Badge>
          <Button
            v-if="EnvironmentSettingsRules.canLink(row)"
            variant="ghost"
            tone="neutral"
            size="sm"
            :aria-label="`Link ${row.key} to another product's key`"
            title="Take the value of another product's key"
            @click="linking = row.key"
          >
            <template #leading><Link2 :size="14" /></template>Link
          </Button>
          <DeleteConfirm
            v-if="row.link"
            size="sm"
            label="Unlink"
            :question="`Unlink ${row.key} from ${row.link.target} · ${row.link.key}? It renders its own value again, if it has one.`"
            :on-confirm="() => links.unlink.mutateAsync({ target: props.plan.target, key: row.key })"
          />
          <DeleteConfirm
            v-if="row.source === 'stored' && !EnvironmentSettingsRules.isLinked(row)"
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
    <LinkKeyModal
      :open="linking !== null"
      :plan="props.plan"
      :setting-key="linking"
      @update:open="(open: boolean) => !open && (linking = null)"
    />
  </div>
</template>
