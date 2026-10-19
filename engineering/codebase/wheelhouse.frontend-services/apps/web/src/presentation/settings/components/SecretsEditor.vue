<script lang="ts">
import type { DeploySettings } from "@/domain/deployments";

/** Defines an environment's secrets editor. */
export interface SecretsEditorProps {
  readonly plan: DeploySettings;
  readonly product: string;
}
</script>

<script setup lang="ts">
import { computed, ref, shallowRef } from "vue";
import { History, KeyRound, Plus } from "lucide-vue-next";
import { Button } from "@wow-two-beta/fe/presentation/actions";
import {
  Badge,
  EmptyState,
  RelativeTime,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
} from "@wow-two-beta/fe/presentation/display";
import { useSettingKeys, useSettingSaver } from "@/application/settings";
import { EnvironmentSettingsRules, type EnvironmentKey } from "@/domain/settings";
import { DeleteConfirm } from "@/presentation/common/components";
import AddKeyModal from "./AddKeyModal.vue";
import SecretHistoryModal from "./SecretHistoryModal.vue";
import SecretValueModal from "./SecretValueModal.vue";

/**
 * The environment's secrets: each declared or held key with its version, who changed it and when, its fingerprint and
 * expiry — never a value. A new value writes the next version with a note; History shows every version and restores
 * one; Remove stops it rendering and keeps its versions.
 */
defineOptions({ name: "SecretsEditor" });
const props = defineProps<SecretsEditorProps>();
defineSlots<{}>();

const saver = useSettingSaver(() => props.plan.target);
const keys = useSettingKeys();
const rows = computed(() => EnvironmentSettingsRules.secrets(props.plan));
const writing = shallowRef<EnvironmentKey | null>(null);
const viewing = ref<string | null>(null);
const adding = ref(false);
</script>

<template>
  <div class="flex flex-col gap-4">
    <div class="flex flex-wrap items-center justify-between gap-3">
      <p class="text-sm text-muted-foreground">Values are write-only. Every change keeps a version for the audit.</p>
      <Button size="sm" @click="adding = true"><template #leading><Plus :size="14" /></template>Add secret</Button>
    </div>
    <EmptyState
      v-if="!rows.length"
      size="sm"
      title="No secrets yet"
      description="The running release declares none; add the ones this environment needs."
    />
    <Table v-else density="compact" is-hoverable container-class-name="overflow-x-auto rounded-xl border border-border">
      <TableHead
        ><TableRow>
          <TableHeaderCell>Key</TableHeaderCell><TableHeaderCell>Version</TableHeaderCell><TableHeaderCell>Changed</TableHeaderCell
          ><TableHeaderCell>Expiry</TableHeaderCell><TableHeaderCell><span class="sr-only">Actions</span></TableHeaderCell>
        </TableRow></TableHead
      >
      <TableBody>
        <TableRow v-for="row in rows" :key="row.key">
          <TableCell>
            <span class="flex flex-col gap-1">
              <span class="font-mono text-xs">{{ row.key }}</span>
              <span class="flex flex-wrap gap-1">
                <Badge v-if="row.required && !row.vault" variant="warning">required</Badge>
                <Badge v-if="row.type !== 'text'" variant="neutral">{{ row.type }}</Badge>
                <Badge v-if="row.adHoc" variant="neutral">added</Badge>
                <Badge v-if="row.unapplied" variant="info">changed</Badge>
              </span>
            </span>
          </TableCell>
          <TableCell>
            <span v-if="row.vault" class="flex flex-col">
              <span>v{{ row.vault.version }}</span>
              <span class="font-mono text-[11px] text-muted-foreground" title="Fingerprint: equal values show equal ones">{{
                row.vault.fingerprint.slice(0, 8)
              }}</span>
            </span>
            <span v-else class="text-xs text-muted-foreground">{{ EnvironmentSettingsRules.sourceLabel(row) }}</span>
          </TableCell>
          <TableCell class="whitespace-nowrap text-xs text-muted-foreground">
            <template v-if="row.vault">{{ row.vault.updatedBy }} · <RelativeTime :value="row.vault.updatedAt" /></template>
          </TableCell>
          <TableCell>
            <Badge
              v-if="EnvironmentSettingsRules.expiryLabel(row.vault?.expiry)"
              :variant="EnvironmentSettingsRules.expiryTone(row.vault?.expiry)"
              >{{ EnvironmentSettingsRules.expiryLabel(row.vault?.expiry) }}</Badge
            >
            <span v-else-if="row.vault?.expiresAt" class="text-xs text-muted-foreground">{{ row.vault.expiresAt.slice(0, 10) }}</span>
          </TableCell>
          <TableCell label="" class="justify-end">
            <div class="flex justify-end gap-1">
              <Button variant="ghost" tone="neutral" size="sm" :aria-label="`Set ${row.key}`" title="Write a new version" @click="writing = row">
                <KeyRound :size="14" />
              </Button>
              <Button
                v-if="row.vault"
                variant="ghost"
                tone="neutral"
                size="sm"
                :aria-label="`History of ${row.key}`"
                title="Versions"
                @click="viewing = row.key"
              >
                <History :size="14" />
              </Button>
              <DeleteConfirm
                v-if="row.vault"
                size="sm"
                label="Remove"
                :question="`Stop rendering ${row.key}? Its versions stay.`"
                :on-confirm="() => saver.save([{ key: row.key, value: '', secret: true, remove: true }])"
              />
              <DeleteConfirm
                v-else-if="row.adHoc"
                size="sm"
                label="Remove key"
                :question="`Remove the key ${row.key} from ${props.product}?`"
                :on-confirm="() => keys.remove.mutateAsync({ product: props.product, key: row.key })"
              />
            </div>
          </TableCell>
        </TableRow>
      </TableBody>
    </Table>
    <SecretValueModal
      :open="writing !== null"
      :target="props.plan.target"
      :environment="props.plan.environment"
      :secret="writing"
      @update:open="(open: boolean) => !open && (writing = null)"
    />
    <SecretHistoryModal
      :open="viewing !== null"
      :target="props.plan.target"
      :secret-key="viewing"
      @update:open="(open: boolean) => !open && (viewing = null)"
    />
    <AddKeyModal v-model:open="adding" :product="props.product" :secret="true" />
  </div>
</template>
