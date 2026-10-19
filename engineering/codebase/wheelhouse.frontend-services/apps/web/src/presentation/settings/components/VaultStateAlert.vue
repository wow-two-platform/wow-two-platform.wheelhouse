<script lang="ts">
import type { DeploySettingsVault } from "@/domain/settings";

/** Defines the notice an environment's editors show while its secrets cannot be read. */
export interface VaultStateAlertProps {
  readonly vault: DeploySettingsVault | null;
}
</script>

<script setup lang="ts">
import { computed } from "vue";
import { Alert } from "@wow-two-beta/fe/presentation/feedback";

/** Says why secrets neither save nor render: a sealed built-in vault, or an unreachable standalone one. */
defineOptions({ name: "VaultStateAlert" });
const props = defineProps<VaultStateAlertProps>();
defineSlots<{}>();

const text = computed(() => {
  const vault = props.vault;
  if (!vault) return null;
  if (vault.sealed === "mismatch")
    return "The vault is sealed: its root key file belongs to another vault. Place this vault's key; secrets neither save nor render until then.";
  if (vault.sealed === "invalid")
    return "The vault is sealed: its root key file holds no base64 256-bit key. Secrets neither save nor render until it does.";
  if (vault.sealed)
    return "The vault is sealed: its root key file is missing. Metadata still reads; secrets neither save nor render, and deploys that need them wait.";
  if (!vault.reachable) return `The vault ${vault.id} is unreachable; deploys that need its secrets wait until it answers.`;
  return null;
});
</script>

<template>
  <Alert v-if="text" severity="warning" title="Secrets are on hold" :description="text" />
</template>
