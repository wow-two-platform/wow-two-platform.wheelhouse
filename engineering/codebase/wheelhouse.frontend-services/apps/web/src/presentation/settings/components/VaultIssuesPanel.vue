<script setup lang="ts">
import { computed } from "vue";
import { RouterLink } from "vue-router";
import { FileKey, KeyRound, LockKeyhole } from "lucide-vue-next";
import { Badge } from "@wow-two-beta/fe/presentation/display";
import { useBuiltInVaultIssues } from "@/application/settings";
import { ConsolePaths } from "@/application/navigation";
import { EnvironmentSettingsRules, RootKeyRules } from "@/domain/settings";
import { Panel } from "@/presentation/common/components";

/**
 * What the built-in vault needs the owner for, beside the deployment issues: a sealed vault that holds secrets (deploys
 * that need them wait), the offline root key copy due a check, a failed check of the root key file, and each secret
 * lapsing within 30 or 7 days or lapsed. Shown only while there is something.
 */
defineOptions({ name: "VaultIssuesPanel" });
defineSlots<{}>();
const issues = useBuiltInVaultIssues();
const sealed = computed(() => issues.data.value?.state === "sealed" && issues.data.value.reason !== null);
const expiries = computed(() => issues.data.value?.expiries ?? []);
const offlineCopy = computed(() => issues.data.value?.offlineCopy ?? null);
const keyFile = computed(() => issues.data.value?.keyFile ?? null);
const total = computed(
  () => expiries.value.length + (sealed.value ? 1 : 0) + (offlineCopy.value ? 1 : 0) + (keyFile.value?.problem ? 1 : 0),
);
const date = (iso: string) => new Date(iso).toLocaleDateString();
const reasonText: Record<string, string> = {
  missing: "its root key file is missing",
  invalid: "its root key file holds no base64 256-bit key",
  mismatch: "its root key file belongs to another vault",
};
</script>

<template>
  <Panel v-if="total" title="Secrets" :description="`${total} ${total === 1 ? 'issue needs' : 'issues need'} attention.`">
    <ul class="flex flex-col divide-y divide-border">
      <li v-if="sealed" class="flex items-start gap-3 py-3 first:pt-0 last:pb-0">
        <LockKeyhole :size="18" class="mt-0.5 text-destructive" aria-hidden="true" />
        <p class="min-w-0 text-sm">
          <span class="font-medium">The vault is sealed:</span>
          {{ reasonText[issues.data.value?.reason ?? "missing"] ?? "its root key cannot be read" }}. Deploys that need
          secrets wait until it is back.
        </p>
      </li>
      <li v-if="keyFile?.problem" class="flex items-start gap-3 py-3 first:pt-0 last:pb-0">
        <FileKey :size="18" class="mt-0.5 text-destructive" aria-hidden="true" />
        <p class="min-w-0 text-sm">
          <span class="font-medium">The root key file check failed</span> {{ new Date(keyFile.at).toLocaleString() }}:
          {{ RootKeyRules.problemText(keyFile.problem) }}.
        </p>
      </li>
      <li v-if="offlineCopy" class="flex items-start gap-3 py-3 first:pt-0 last:pb-0">
        <KeyRound :size="18" class="mt-0.5 text-warning" aria-hidden="true" />
        <p class="min-w-0 text-sm">
          <RouterLink :to="ConsolePaths.secrets" class="font-medium hover:text-primary">Check your offline root key copy</RouterLink>:
          {{ RootKeyRules.offlineCopyText(offlineCopy, date) }}
        </p>
      </li>
      <li v-for="item in expiries" :key="`${item.product}/${item.environment}/${item.key}`" class="flex items-start gap-3 py-3 first:pt-0 last:pb-0">
        <KeyRound
          :size="18"
          :class="['mt-0.5', item.level === 'expired' ? 'text-destructive' : 'text-warning']"
          aria-hidden="true"
        />
        <p class="flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1 text-sm">
          <RouterLink
            :to="{ path: ConsolePaths.product(item.product, 'secrets'), query: { env: item.environment } }"
            class="font-medium hover:text-primary"
            >{{ item.product }}</RouterLink
          >
          <Badge :variant="item.environment === 'prod' ? 'info' : 'neutral'">{{ item.environment }}</Badge>
          <span class="font-mono text-xs">{{ item.key }}</span>
          <Badge :variant="EnvironmentSettingsRules.expiryTone(item.level)">{{ EnvironmentSettingsRules.expiryLabel(item.level) }}</Badge>
          <span class="text-xs text-muted-foreground">{{ item.expiresAt.slice(0, 10) }}</span>
        </p>
      </li>
    </ul>
  </Panel>
</template>
