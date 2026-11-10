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
import { History, KeyRound, Link2, Plus, RefreshCw } from "lucide-vue-next";
import { Button } from "@wow-two-beta/fe/presentation/actions";
import { Alert } from "@wow-two-beta/fe/presentation/feedback";
import { ConfirmPopover } from "@wow-two-beta/fe/presentation/overlays";
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
import { useRegenerateSecret, useSettingKeys, useSettingLinks, useSettingSaver } from "@/application/settings";
import { EnvironmentSettingsRules, type EnvironmentKey } from "@/domain/settings";
import { DeleteConfirm } from "@/presentation/common/components";
import { failureReason } from "@/integration/common";
import AddKeyModal from "./AddKeyModal.vue";
import LinkKeyModal from "./LinkKeyModal.vue";
import SecretHistoryModal from "./SecretHistoryModal.vue";
import SecretValueModal from "./SecretValueModal.vue";

/**
 * The environment's secrets: each declared or held key with its version, who changed it and when, its fingerprint and
 * expiry — never a value. A new value writes the next version with a note; History shows every version and restores
 * one; Remove stops it rendering and keeps its versions. A secret the release has Wheelhouse make offers Regenerate
 * instead of a typed value: a new version the next deploy applies. A linked secret shows its source, read-only; one
 * linked to another product's key shows that key's version and fingerprint, and Unlink gives it back its own. Link takes
 * a secret from another product's key in the same environment, such as a client secret that product's vault generates.
 */
defineOptions({ name: "SecretsEditor" });
const props = defineProps<SecretsEditorProps>();
defineSlots<{}>();

const saver = useSettingSaver(() => props.plan.target);
const keys = useSettingKeys();
const links = useSettingLinks();
const regenerate = useRegenerateSecret();
const linking = ref<string | null>(null);
const rows = computed(() => EnvironmentSettingsRules.secrets(props.plan));
const unread = computed(() => EnvironmentSettingsRules.unread(props.plan));
/** Why the last regeneration failed, by key: the runner's error line, never a value. */
const failures = ref<Record<string, string>>({});

/** Has Wheelhouse make a new version of a generated secret; a failure stays on its row. @internal */
async function regenerateKey(key: string): Promise<void> {
  failures.value = Object.fromEntries(Object.entries(failures.value).filter(([known]) => known !== key));
  const result = await regenerate.mutateAsync({ target: props.plan.target, key });
  if (!result.ok) failures.value = { ...failures.value, [key]: failureReason(result.failure) || "Not regenerated." };
}
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
    <Alert
      v-if="unread.length"
      severity="warning"
      :title="`${unread.length} generated client ${unread.length === 1 ? 'secret' : 'secrets'} nothing reads`"
    >
      <p class="text-sm">
        No key of this release takes {{ unread.length === 1 ? "it" : "them" }} with <code>from</code> and no other product
        links {{ unread.length === 1 ? "it" : "them" }}, so no client can present
        {{ unread.length === 1 ? "it" : "them" }}: link each from the product that signs in with it, or drop its
        registration.
      </p>
      <p class="mt-1 font-mono text-xs break-all">{{ unread.map((key) => key.key).join(", ") }}</p>
    </Alert>
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
                <Badge v-if="EnvironmentSettingsRules.canRegenerate(row)" variant="neutral" :title="EnvironmentSettingsRules.generateLabel(row.generate) ?? undefined"
                  >generated</Badge
                >
                <Badge v-if="EnvironmentSettingsRules.isLinked(row)" variant="neutral">linked</Badge>
                <Badge v-if="row.unread" variant="warning" title="Generated, and nothing reads it: no from, no link from another product">unread</Badge>
                <Badge v-if="row.held" variant="info" title="No service of this release declares it; it waits for a release that does">held</Badge>
              </span>
              <span v-if="row.link && row.vault" class="flex items-center gap-1 text-xs text-muted-foreground"
                ><Link2 :size="12" aria-hidden="true" />{{ EnvironmentSettingsRules.sourceLabel(row) }}</span
              >
              <span v-if="failures[row.key]" class="text-xs text-danger" role="alert">{{ failures[row.key] }}</span>
            </span>
          </TableCell>
          <TableCell>
            <span v-if="row.vault" class="flex flex-col">
              <span>v{{ row.vault.version }}</span>
              <span class="font-mono text-[11px] text-muted-foreground" title="Fingerprint: equal values show equal ones">{{
                row.vault.fingerprint.slice(0, 8)
              }}</span>
            </span>
            <span v-else class="flex items-center gap-1 text-xs text-muted-foreground"
              ><Link2 v-if="EnvironmentSettingsRules.isLinked(row)" :size="12" aria-hidden="true" />{{
                EnvironmentSettingsRules.sourceLabel(row)
              }}</span
            >
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
            <div v-if="!EnvironmentSettingsRules.isLinked(row)" class="flex justify-end gap-1">
              <ConfirmPopover
                v-if="EnvironmentSettingsRules.canRegenerate(row)"
                :title="`Regenerate ${row.key}?`"
                :description="`Wheelhouse makes a new value (${EnvironmentSettingsRules.generateLabel(row.generate)}) as the next version; the next deploy applies it.`"
                confirm-label="Regenerate"
                :on-confirm="() => regenerateKey(row.key)"
              >
                <Button variant="ghost" tone="neutral" size="sm" :aria-label="`Regenerate ${row.key}`" title="Generate a new version">
                  <RefreshCw :size="14" />
                </Button>
              </ConfirmPopover>
              <Button
                v-else
                variant="ghost"
                tone="neutral"
                size="sm"
                :aria-label="`Set ${row.key}`"
                title="Write a new version"
                @click="writing = row"
              >
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
              <Button
                v-if="EnvironmentSettingsRules.canLink(row)"
                variant="ghost"
                tone="neutral"
                size="sm"
                :aria-label="`Link ${row.key} to another product's key`"
                title="Take the value of another product's key"
                @click="linking = row.key"
              >
                <Link2 :size="14" />
              </Button>
            </div>
            <div v-else-if="row.link" class="flex justify-end gap-1">
              <DeleteConfirm
                size="sm"
                label="Unlink"
                :question="`Unlink ${row.key} from ${row.link.target} · ${row.link.key}? It renders its own value again, if it has one.`"
                :on-confirm="() => links.unlink.mutateAsync({ target: props.plan.target, key: row.key })"
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
    <LinkKeyModal
      :open="linking !== null"
      :plan="props.plan"
      :setting-key="linking"
      @update:open="(open: boolean) => !open && (linking = null)"
    />
  </div>
</template>
