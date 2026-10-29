<script lang="ts">
import type { Server } from "@/domain/servers";

/** Defines one enrolled server's Kit section. */
export interface ServerUpkeepProps {
  readonly server: Server;
}
</script>

<script setup lang="ts">
import { computed, ref } from "vue";
import { pluralize } from "@wow-two-beta/fe/foundation/formatters";
import { RefreshCw } from "lucide-vue-next";
import { Button } from "@wow-two-beta/fe/presentation/actions";
import { RelativeTime } from "@wow-two-beta/fe/presentation/display";
import { Alert } from "@wow-two-beta/fe/presentation/feedback";
import { useTailnet, useUpkeep, useUpkeepChanges } from "@/application/infrastructure";
import { InfraJobRules, InfraJobStatus, UpkeepRules } from "@/domain/infrastructure";
import { failureReason } from "@/integration/common";
import { StatusPill } from "@/presentation/common/components";
import JobSteps from "./JobSteps.vue";

/**
 * An enrolled server's kit: whether the host runs the kit this console ships, its public address, and Refresh kit —
 * a recorded run that uploads the kit, lays it out with its firewall (80 and 443 public, SSH as it is), brings
 * PostgreSQL and the ingress up and checks the public entry points, its steps read every two seconds while it runs. On
 * deployed dev it says the refresh runs on prod only.
 */
defineOptions({ name: "ServerUpkeep" });
const props = defineProps<ServerUpkeepProps>();
defineSlots<{}>();

const visible = computed(() => UpkeepRules.isVisible(props.server));
const tailnet = useTailnet();
const read = useUpkeep(() => props.server.slug, visible);
const operations = useUpkeepChanges();
const starting = ref(false);
const startError = ref("");
const listing = computed(() => read.data.value ?? null);
const latest = computed(() => UpkeepRules.latest(listing.value));
const status = computed(() => latest.value?.status ?? null);
const running = computed(() => InfraJobRules.isActive(status.value));
const blocked = computed(() =>
  tailnet.data.value?.prodOnly ? (tailnet.data.value.reason ?? "Kit refresh runs on prod only.") : null,
);
const hostKit = computed(() => UpkeepRules.hostKit(listing.value));
const publicAddress = computed(() => props.server.ingress.publicAddress ?? latest.value?.facts.publicAddress ?? null);

/** Starts a run; a refusal shows under the heading. */
async function refresh(): Promise<void> {
  if (starting.value) return;
  starting.value = true;
  startError.value = "";
  try {
    const result = await operations.start(props.server.slug);
    if (!result.ok) startError.value = failureReason(result.failure);
  } finally {
    starting.value = false;
  }
}
</script>

<template>
  <section
    v-if="visible"
    :aria-label="`${props.server.name} kit`"
    class="flex flex-col gap-3 rounded-xl border border-border bg-background/40 p-3"
  >
    <div class="flex flex-wrap items-center gap-2">
      <h3 class="text-sm font-medium">Kit</h3>
      <StatusPill v-if="status" :state="InfraJobRules.state(status)" :label="InfraJobRules.word(status)" />
      <span v-if="latest?.completedAt" class="text-xs text-muted-foreground"
        ><RelativeTime :value="latest.completedAt"
      /></span>
      <Button
        size="sm"
        variant="outline"
        tone="neutral"
        class="ms-auto max-md:min-h-11"
        :is-loading="starting"
        :is-disabled="Boolean(blocked) || !UpkeepRules.canStart(listing)"
        :title="blocked ?? undefined"
        @click="refresh"
      >
        <template #leading><RefreshCw :size="14" /></template>
        Refresh kit
      </Button>
    </div>
    <p v-if="blocked" class="text-xs text-muted-foreground">{{ blocked }}</p>
    <Alert v-if="startError" severity="danger" :description="startError" />
    <Alert
      v-if="latest?.reason && !running && status !== InfraJobStatus.Succeeded"
      :severity="status === InfraJobStatus.Interrupted ? 'warning' : 'danger'"
      :description="latest.reason"
    />
    <dl class="grid gap-x-4 gap-y-2 text-sm sm:grid-cols-2">
      <div class="min-w-0">
        <dt class="text-xs text-muted-foreground">On the host</dt>
        <dd v-if="hostKit" class="font-mono text-xs" :title="hostKit">
          {{ InfraJobRules.shortDigest(hostKit)
          }}<span class="ms-2 font-sans text-muted-foreground">{{
            UpkeepRules.isCurrent(listing) ? "current" : "older"
          }}</span>
        </dd>
        <dd v-else class="text-xs text-muted-foreground">Unknown until a refresh succeeds</dd>
      </div>
      <div class="min-w-0">
        <dt class="text-xs text-muted-foreground">Public address</dt>
        <dd v-if="publicAddress" class="font-mono text-xs">
          {{ publicAddress
          }}<span v-if="UpkeepRules.sourceWord(latest?.facts.addressSource)" class="ms-2 font-sans text-muted-foreground">{{
            UpkeepRules.sourceWord(latest?.facts.addressSource)
          }}</span>
        </dd>
        <dd v-else class="text-xs text-muted-foreground">Not recorded</dd>
      </div>
    </dl>
    <JobSteps
      v-if="latest?.steps.length && status !== InfraJobStatus.Succeeded"
      :steps="latest.steps"
      :label="`${props.server.name} kit refresh steps`"
    />
    <details v-else-if="latest?.steps.length" class="text-sm">
      <summary class="cursor-pointer text-xs font-medium text-muted-foreground max-md:py-2">
        {{ latest.steps.length }} {{ pluralize(latest.steps.length, "step") }}
      </summary>
      <JobSteps class="mt-2" :steps="latest.steps" :label="`${props.server.name} kit refresh steps`" />
    </details>
  </section>
</template>
