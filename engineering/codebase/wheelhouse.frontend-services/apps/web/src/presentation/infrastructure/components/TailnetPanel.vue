<script setup lang="ts">
import { computed, ref } from "vue";
import { pluralize } from "@wow-two-beta/fe/foundation/formatters";
import { FileCheck2 } from "lucide-vue-next";
import { Button } from "@wow-two-beta/fe/presentation/actions";
import { DataRegion } from "@wow-two-beta/fe/presentation/feedback";
import { useTailnet } from "@/application/infrastructure";
import {
  TAILNET_CLIENT_TAG,
  TAILNET_SCOPES,
  TailnetConnection,
  TailnetRules,
} from "@/domain/infrastructure";
import { Panel, StatusPill } from "@/presentation/common/components";
import PolicyReviewModal from "./PolicyReviewModal.vue";

/**
 * Whether Wheelhouse reaches the tailnet through its OAuth client: connected (with Review policy), not configured (the
 * owner's one-time step), refused (Tailscale's reason) or prod only on deployed dev. It polls the shared status every
 * minute for every server panel's node.
 */
defineOptions({ name: "TailnetPanel" });
defineSlots<{}>();

const tailnet = useTailnet(true);
const reviewing = ref(false);
const status = computed(() => tailnet.data.value ?? null);
const connection = computed(() => (status.value ? TailnetRules.connectionOf(status.value) : null));
const online = computed(() => status.value?.nodes.filter((node) => node.online).length ?? 0);
</script>

<template>
  <Panel title="Tailnet">
    <template #actions>
      <Button
        v-if="connection === TailnetConnection.Connected"
        size="sm"
        variant="outline"
        tone="neutral"
        class="max-md:min-h-11"
        @click="reviewing = true"
      >
        <template #leading><FileCheck2 :size="14" /></template>Review policy
      </Button>
    </template>
    <DataRegion
      :is-first-load="tailnet.isFirstLoad.value"
      :error="status ? null : tailnet.error.value"
      :has-data="Boolean(status)"
      loading-label="Reading the tailnet"
      @retry="tailnet.refetch()"
    >
      <div v-if="status" class="flex flex-col gap-3 text-sm">
        <p class="flex flex-wrap items-center gap-x-3 gap-y-1.5">
          <StatusPill
            v-if="connection === TailnetConnection.Connected"
            state="healthy"
            label="Connected"
            :detail="status.tailnet"
          />
          <StatusPill v-else-if="connection === TailnetConnection.ProdOnly" state="none" label="Prod only" />
          <StatusPill v-else-if="connection === TailnetConnection.Refused" state="failed" label="Refused" />
          <StatusPill v-else state="none" label="Not configured" />
          <span v-if="connection === TailnetConnection.Connected" class="text-muted-foreground"
            >{{ status.nodes.length }} {{ pluralize(status.nodes.length, "node") }} · {{ online }} online</span
          >
        </p>
        <p v-if="connection === TailnetConnection.ProdOnly || connection === TailnetConnection.Refused" class="text-muted-foreground">
          {{ status.reason ?? (connection === TailnetConnection.ProdOnly ? "Tailscale runs on prod only." : "Tailscale did not accept the OAuth client.") }}
        </p>
        <ol v-if="connection === TailnetConnection.NotConfigured" class="list-decimal space-y-1 ps-5">
          <li>
            Create a Tailscale OAuth client with scopes
            <template v-for="(scope, index) in TAILNET_SCOPES" :key="scope"
              ><code class="rounded bg-muted px-1 font-mono text-xs">{{ scope }}</code
              >{{ index < TAILNET_SCOPES.length - 2 ? ", " : index === TAILNET_SCOPES.length - 2 ? " and " : "" }}</template
            >, tag <code class="rounded bg-muted px-1 font-mono text-xs">{{ TAILNET_CLIENT_TAG }}</code>.
          </li>
          <li>
            Place it at
            <code class="rounded bg-muted px-1 font-mono text-xs break-all">{{ TailnetRules.clientPath(status) }}</code>,
            mode 0600.
          </li>
        </ol>
      </div>
    </DataRegion>
    <PolicyReviewModal :open="reviewing" @update:open="reviewing = $event" />
  </Panel>
</template>
