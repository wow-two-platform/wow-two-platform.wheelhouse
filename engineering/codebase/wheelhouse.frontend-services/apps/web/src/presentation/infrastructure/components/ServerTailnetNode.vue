<script lang="ts">
import type { Server } from "@/domain/servers";

/** Defines one server's tailnet node row. */
export interface ServerTailnetNodeProps {
  readonly server: Server;
}
</script>

<script setup lang="ts">
import { computed } from "vue";
import { Badge, RelativeTime } from "@wow-two-beta/fe/presentation/display";
import { useTailnet } from "@/application/infrastructure";
import { TailnetRules } from "@/domain/infrastructure";
import { VpsProvider } from "@/domain/servers";

/**
 * The server's node on the tailnet while Wheelhouse is connected: its name and hostname, tailnet address, whether it
 * is online (else when it was last seen), its tags and its key's expiry. A connected tailnet without the server's node
 * says so; a tailnet that is not connected draws nothing here.
 */
defineOptions({ name: "ServerTailnetNode" });
const props = defineProps<ServerTailnetNodeProps>();
defineSlots<{}>();

const tailnet = useTailnet();
/** Whether to draw the row: the tailnet is connected and the server is not the local rig, which never joins it. */
const connected = computed(
  () => tailnet.data.value?.connected === true && !tailnet.data.value.prodOnly && props.server.provider !== VpsProvider.Local,
);
const node = computed(() => TailnetRules.nodeOf(tailnet.data.value, props.server.slug));
const address = computed(() => (node.value ? TailnetRules.addressOf(node.value) : null));
</script>

<template>
  <section v-if="connected" :aria-label="`${props.server.name} tailnet node`">
    <h3 class="mb-2 text-xs font-medium text-muted-foreground">Tailnet</h3>
    <div
      v-if="node"
      class="flex flex-wrap items-center gap-x-3 gap-y-1.5 rounded-xl border border-border bg-background/40 px-3 py-2 text-sm"
    >
      <span class="min-w-0 font-medium break-all"
        >{{ node.name }}<span v-if="node.hostname && node.hostname !== node.name" class="ms-1.5 font-normal text-muted-foreground">{{
          node.hostname
        }}</span></span
      >
      <span v-if="address" class="font-mono text-xs">{{ address }}</span>
      <Badge :variant="node.online ? 'success' : 'neutral'" size="sm">{{ node.online ? "Online" : "Offline" }}</Badge>
      <span v-if="!node.online && node.lastSeen" class="text-xs text-muted-foreground"
        >last seen <RelativeTime :value="node.lastSeen"
      /></span>
      <span class="flex flex-wrap gap-1">
        <Badge v-for="tag in node.tags" :key="tag" variant="outline" size="sm">{{ tag }}</Badge>
      </span>
      <span class="text-xs text-muted-foreground sm:ms-auto">
        <template v-if="node.keyExpiryDisabled">Key never expires</template>
        <template v-else-if="node.expires">Key expires <RelativeTime :value="node.expires" /></template>
      </span>
    </div>
    <p v-else class="text-sm text-muted-foreground">No tailnet node.</p>
  </section>
</template>
