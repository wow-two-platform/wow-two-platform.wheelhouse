<script lang="ts">
import type { MintedKey } from "@/domain/infrastructure";

/** Defines the single-use tailnet keys a job minted. */
export interface MintedKeysProps {
  readonly mints: readonly MintedKey[];
}
</script>

<script setup lang="ts">
import { Badge } from "@wow-two-beta/fe/presentation/display";
import { Measures } from "@/domain/common";

/** Each minted key's ID, tags, node and expiry; the key itself never leaves the control host. */
defineOptions({ name: "MintedKeys" });
const props = defineProps<MintedKeysProps>();
defineSlots<{}>();
</script>

<template>
  <section aria-label="Minted keys">
    <h4 class="mb-1.5 text-xs font-medium text-muted-foreground">Minted keys</h4>
    <ul role="list" class="flex flex-col gap-1.5">
      <li
        v-for="mint in props.mints"
        :key="mint.keyId"
        class="flex flex-wrap items-center gap-x-2 gap-y-1 text-xs"
      >
        <span class="font-mono">{{ mint.keyId }}</span>
        <Badge v-for="tag in mint.tags" :key="tag" variant="outline" size="sm">{{ tag }}</Badge>
        <span v-if="mint.node" class="text-muted-foreground">{{ mint.node }}</span>
        <span class="text-muted-foreground sm:ms-auto">expires {{ Measures.moment(mint.expires) }}</span>
      </li>
    </ul>
  </section>
</template>
