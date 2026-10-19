<script lang="ts">
import type { OneTimeKey } from "@/domain/infrastructure";

/** Defines the one-time enrollment key and how the owner adds it. */
export interface OneTimeKeyBlockProps {
  /** The key's public half. */
  readonly oneTimeKey: OneTimeKey;
  /** The account the server's image delivers, which the key signs in as. */
  readonly loginUser: string;
  /** The server's public address, for the one-line command. */
  readonly address: string;
}
</script>

<script setup lang="ts">
import { computed } from "vue";
import { CopyButton } from "@wow-two-beta/fe/presentation/actions";
import { CodeText } from "@wow-two-beta/fe/presentation/display";
import { EnrollmentRules } from "@/domain/infrastructure";

/**
 * The one-time key's public half, to copy, and where it goes: the sign-in account — root on a root-only image such as
 * Contabo — at order time, or once with the command shown. Only the public half ever reaches the browser.
 */
defineOptions({ name: "OneTimeKeyBlock" });
const props = defineProps<OneTimeKeyBlockProps>();
defineSlots<{}>();

const command = computed(() =>
  EnrollmentRules.authorizeCommand(props.oneTimeKey.publicKey, props.loginUser || "root", props.address),
);
</script>

<template>
  <div class="flex flex-col gap-2 text-sm">
    <div class="flex items-start gap-2">
      <CodeText class="min-w-0 flex-1 text-xs break-all">{{ props.oneTimeKey.publicKey }}</CodeText>
      <CopyButton
        :text="props.oneTimeKey.publicKey"
        size="sm"
        variant="outline"
        tone="neutral"
        shape="square"
        class="max-md:size-11"
        aria-label="Copy the one-time key"
        copied-aria-label="One-time key copied"
      />
    </div>
    <p class="font-mono text-xs break-all text-muted-foreground">{{ props.oneTimeKey.fingerprint }}</p>
    <p>Add it to the sign-in account, {{ props.loginUser || "root" }}, at order time, or once with:</p>
    <div class="flex items-start gap-2">
      <CodeText class="min-w-0 flex-1 text-xs break-all">{{ command }}</CodeText>
      <CopyButton
        :text="command"
        size="sm"
        variant="outline"
        tone="neutral"
        shape="square"
        class="max-md:size-11"
        aria-label="Copy the command"
        copied-aria-label="Command copied"
      />
    </div>
  </div>
</template>
