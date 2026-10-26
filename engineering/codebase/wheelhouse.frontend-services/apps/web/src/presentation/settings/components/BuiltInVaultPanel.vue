<script setup lang="ts">
import { computed, ref } from "vue";
import { KeyRound, ShieldCheck } from "lucide-vue-next";
import { Button, CopyButton } from "@wow-two-beta/fe/presentation/actions";
import { Badge, CodeText, DescriptionGroup } from "@wow-two-beta/fe/presentation/display";
import { Alert, DataRegion } from "@wow-two-beta/fe/presentation/feedback";
import { Field, TextInput } from "@wow-two-beta/fe/presentation/forms";
import { useBuiltInVault, useVaultOperations } from "@/application/settings";
import { useVaults } from "@/application/secrets";
import { RootKeyRules } from "@/domain/settings";
import { failureReason } from "@/integration/common";
import { Panel } from "@/presentation/common/components";

/**
 * The built-in Secrets Vault: unsealed or sealed and why, the root key file it reads (the path, never the key), what it
 * holds, the root key's two checks — its fingerprint to compare with the owner's offline copy, marked verified with a
 * reason, and the scheduled check of the key file — the full decryption check, the proof after a restore, and the import
 * of each standalone vault still listed, over its management API. Results are counts, key names and the fingerprint,
 * never a value or the key.
 */
defineOptions({ name: "BuiltInVaultPanel" });
defineSlots<{}>();
const status = useBuiltInVault();
const standalone = useVaults();
const operations = useVaultOperations();

const reasonText: Record<string, string> = {
  missing: "The root key file is missing: place it, root-only, and the vault unseals without a restart.",
  invalid: "The root key file holds no base64 256-bit key.",
  mismatch: "The root key file belongs to another vault: put this vault's key back.",
};
const date = (iso: string) => new Date(iso).toLocaleDateString();
const offline = computed(() => status.data.value?.offlineCopy ?? null);
const fileCheck = computed(() => status.data.value?.keyFileCheck ?? null);
const fileCheckText = computed(() => {
  const check = fileCheck.value;
  if (!check) return "Not run yet";
  const at = new Date(check.at).toLocaleString();
  return check.problem ? `Failed ${at}: ${RootKeyRules.problemText(check.problem)}` : `Passed ${at}`;
});
const facts = computed(() => {
  const data = status.data.value;
  if (!data) return [];
  return [
    { label: "Key file", value: data.keyFile },
    { label: "Products", value: String(data.products) },
    { label: "Secrets", value: String(data.secrets) },
    { label: "Expiring", value: `${data.expiring} within 30 days · ${data.expired} expired` },
    ...(offline.value ? [{ label: "Offline copy", value: RootKeyRules.offlineCopyText(offline.value, date) }] : []),
    { label: "Key file check", value: fileCheckText.value },
  ];
});

/** Marking the offline copy verified: the owner says how they checked it; the fingerprint they compared goes along. */
const marking = ref(false);
const reason = ref("");
async function markVerified(): Promise<void> {
  const fingerprint = offline.value?.fingerprint;
  if (!fingerprint || !reason.value.trim()) return;
  const result = await operations.offlineCopy.mutateAsync({ fingerprint, reason: reason.value.trim() });
  if (!result.ok) return;
  marking.value = false;
  reason.value = "";
}
const verification = computed(() => operations.verify.data.value ?? null);
const imported = computed(() => operations.importFrom.data.value ?? null);
const reachable = computed(() => (standalone.data.value ?? []).filter((vault) => vault.status === "unsealed"));
</script>

<template>
  <Panel title="Built-in vault" description="Secrets every deploy renders, in Wheelhouse's own database under a root key kept apart.">
    <template #actions>
      <Badge v-if="status.data.value" :variant="status.data.value.state === 'unsealed' ? 'success' : 'warning'">{{
        status.data.value.state
      }}</Badge>
      <Badge v-if="status.data.value?.mode === 'external'" variant="info">deploys still read the standalone vaults</Badge>
    </template>
    <DataRegion
      :is-first-load="status.isFirstLoad.value"
      :error="status.error.value"
      :has-data="Boolean(status.data.value)"
      loading-label="Reading the vault"
    >
      <div class="flex flex-col gap-4">
        <Alert
          v-if="status.data.value?.reason"
          severity="warning"
          title="Sealed"
          :description="reasonText[status.data.value.reason] ?? 'The root key cannot be read.'"
        />
        <DescriptionGroup layout="stacked" density="md" class="grid grid-cols-1 gap-x-6 sm:grid-cols-2 [&_dt]:text-xs" :items="facts" />
        <Alert
          v-if="offline?.due"
          severity="warning"
          title="Check your offline copy"
          description="Print your copy's fingerprint with the command below on the machine that holds it, compare, then mark it verified."
        />
        <Alert
          v-if="fileCheck?.problem"
          severity="danger"
          title="The root key file check failed"
          :description="`${RootKeyRules.problemText(fileCheck.problem)}. It clears on the next check that passes.`"
        />
        <section v-if="offline?.fingerprint" class="flex flex-col gap-2 rounded-md bg-muted p-3" aria-label="Root key fingerprint">
          <p class="text-xs text-muted-foreground">Root key fingerprint</p>
          <CodeText class="w-fit text-base">{{ offline.fingerprint }}</CodeText>
          <p class="text-xs text-muted-foreground">Your copy's, printed where it is kept (key from the file or a hidden prompt):</p>
          <div class="flex items-start gap-2 font-mono text-xs">
            <span class="min-w-0 flex-1 break-all">{{ RootKeyRules.command }}</span>
            <CopyButton
              :text="RootKeyRules.command"
              size="sm"
              aria-label="Copy the fingerprint command"
              copied-aria-label="Fingerprint command copied"
            />
          </div>
          <div v-if="marking" class="flex flex-col gap-2">
            <Field label="How you checked it" helper="Kept in the audit trail, such as compared the printed copy" is-required>
              <TextInput v-model="reason" autocomplete="off" maxlength="300" />
            </Field>
            <div class="flex gap-2">
              <Button size="sm" :is-loading="operations.offlineCopy.loading.value" :is-disabled="!reason.trim()" @click="markVerified"
                >It matches: mark verified</Button
              >
              <Button size="sm" variant="ghost" tone="neutral" @click="marking = false">Cancel</Button>
            </div>
          </div>
        </section>
        <div class="flex flex-wrap items-center gap-2">
          <Button size="sm" variant="outline" tone="neutral" :is-loading="operations.verify.loading.value" @click="operations.verify.mutateAsync()">
            <template #leading><ShieldCheck :size="14" aria-hidden="true" /></template>Check every secret decrypts
          </Button>
          <Button v-if="offline?.fingerprint && !marking" size="sm" variant="outline" tone="neutral" @click="marking = true">
            <template #leading><KeyRound :size="14" aria-hidden="true" /></template>Offline copy verified
          </Button>
          <Button
            v-for="vault in reachable"
            :key="vault.id"
            size="sm"
            variant="outline"
            tone="neutral"
            :is-loading="operations.importFrom.loading.value"
            @click="operations.importFrom.mutateAsync(vault.id)"
            >Import from {{ vault.name }}</Button
          >
        </div>
        <Alert
          v-if="verification"
          :severity="verification.failed || !verification.canary ? 'danger' : 'success'"
          :title="verification.failed || !verification.canary ? 'Not everything decrypted' : 'Everything decrypted'"
          :description="`Canary ${verification.canary ? 'verified' : 'not verified'}; ${verification.products} product keys, ${verification.secrets} secrets, ${verification.versions} versions decrypted, ${verification.failed} failed${verification.failures.length ? `: ${verification.failures.join(', ')}` : ''}.`"
        />
        <Alert
          v-if="imported"
          :severity="imported.failed ? 'danger' : 'success'"
          :title="imported.failed ? 'Import kept nothing' : 'Imported'"
          :description="`${imported.namespaces} namespaces matched, ${imported.secrets} secrets and ${imported.versions} versions imported, ${imported.verified} verified, ${imported.skipped} already here${imported.unmatched.length ? `; left alone: ${imported.unmatched.join(', ')}` : ''}${imported.failures.length ? `; failed: ${imported.failures.join(', ')}` : ''}.`"
        />
        <Alert
          v-if="operations.verify.error.value || operations.importFrom.error.value || operations.offlineCopy.error.value"
          severity="danger"
          :description="
            failureReason(operations.verify.error.value ?? operations.importFrom.error.value ?? operations.offlineCopy.error.value)
          "
        />
      </div>
    </DataRegion>
  </Panel>
</template>
