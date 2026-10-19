<script setup lang="ts">
import { computed } from "vue";
import { ShieldCheck } from "lucide-vue-next";
import { Button } from "@wow-two-beta/fe/presentation/actions";
import { Badge, DescriptionGroup } from "@wow-two-beta/fe/presentation/display";
import { Alert, DataRegion } from "@wow-two-beta/fe/presentation/feedback";
import { useBuiltInVault, useVaultOperations } from "@/application/settings";
import { useVaults } from "@/application/secrets";
import { failureReason } from "@/integration/common";
import { Panel } from "@/presentation/common/components";

/**
 * The built-in Secrets Vault: unsealed or sealed and why, the root key file it reads (the path, never the key), what it
 * holds, the full decryption check — the proof after a restore — and the import of each standalone vault still listed,
 * over its management API. Results are counts and key names, never a value.
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
const facts = computed(() => {
  const data = status.data.value;
  if (!data) return [];
  return [
    { label: "Key file", value: data.keyFile },
    { label: "Products", value: String(data.products) },
    { label: "Secrets", value: String(data.secrets) },
    { label: "Expiring", value: `${data.expiring} within 30 days · ${data.expired} expired` },
  ];
});
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
        <div class="flex flex-wrap items-center gap-2">
          <Button size="sm" variant="outline" tone="neutral" :is-loading="operations.verify.loading.value" @click="operations.verify.mutateAsync()">
            <template #leading><ShieldCheck :size="14" aria-hidden="true" /></template>Check every secret decrypts
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
          v-if="operations.verify.error.value || operations.importFrom.error.value"
          severity="danger"
          :description="failureReason(operations.verify.error.value ?? operations.importFrom.error.value)"
        />
      </div>
    </DataRegion>
  </Panel>
</template>
