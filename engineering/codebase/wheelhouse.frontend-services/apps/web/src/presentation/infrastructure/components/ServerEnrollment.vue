<script lang="ts">
import type { Server } from "@/domain/servers";

/** Defines one server's Enrollment section. */
export interface ServerEnrollmentProps {
  readonly server: Server;
}
</script>

<script setup lang="ts">
import { computed, ref } from "vue";
import { RotateCcw, Rocket } from "lucide-vue-next";
import { Button } from "@wow-two-beta/fe/presentation/actions";
import { RelativeTime } from "@wow-two-beta/fe/presentation/display";
import { Alert } from "@wow-two-beta/fe/presentation/feedback";
import { useEnrollment, useEnrollmentChanges, useTailnet } from "@/application/infrastructure";
import { EnrollmentRules, InfraJobRules, InfraJobStatus } from "@/domain/infrastructure";
import { StatusPill } from "@/presentation/common/components";
import EnrollmentModal from "./EnrollmentModal.vue";
import JobSteps from "./JobSteps.vue";
import MintedKeys from "./MintedKeys.vue";
import OneTimeKeyBlock from "./OneTimeKeyBlock.vue";

/**
 * A server's enrollment: shown once one exists, or while the server is still reached on a public address; the control
 * host, which the consoles run on, says so instead, since enrollment sets up fresh public servers. It holds
 * the one-time key and where it goes, Enroll or Rerun, the live steps while it runs (read every two seconds), and once
 * it succeeded the host key and who confirmed it, the tailnet address and node, the kit and the keys it minted —
 * nothing secret. On deployed dev it says enrollment runs on prod only.
 */
defineOptions({ name: "ServerEnrollment" });
const props = defineProps<ServerEnrollmentProps>();
defineSlots<{}>();

const tailnet = useTailnet();
const read = useEnrollment(() => props.server.slug);
const operations = useEnrollmentChanges();
const enrolling = ref(false);
const enrollment = computed(() => read.data.value ?? null);
const status = computed(() => enrollment.value?.status ?? InfraJobStatus.NotStarted);
const visible = computed(() => EnrollmentRules.isVisible(props.server, enrollment.value));
const succeeded = computed(() => status.value === InfraJobStatus.Succeeded);
const blocked = computed(() =>
  tailnet.data.value?.prodOnly ? (tailnet.data.value.reason ?? "Enrollment runs on prod only.") : null,
);
const loginUser = computed(
  () => enrollment.value?.request?.loginUser ?? EnrollmentRules.loginUserFor(props.server.provider),
);
const address = computed(
  () => enrollment.value?.request?.publicAddress ?? EnrollmentRules.publicAddressOf(props.server),
);
const facts = computed(() => enrollment.value?.facts ?? {});
</script>

<template>
  <section
    v-if="visible"
    :aria-label="`${props.server.name} enrollment`"
    class="flex flex-col gap-3 rounded-xl border border-border bg-background/40 p-3"
  >
    <div class="flex flex-wrap items-center gap-2">
      <h3 class="text-sm font-medium">Enrollment</h3>
      <StatusPill :state="InfraJobRules.state(status)" :label="InfraJobRules.word(status)" />
      <span v-if="enrollment?.attempt && enrollment.attempt > 1" class="text-xs text-muted-foreground"
        >attempt {{ enrollment.attempt }}</span
      >
      <span v-if="enrollment?.completedAt" class="text-xs text-muted-foreground"
        ><RelativeTime :value="enrollment.completedAt"
      /></span>
      <Button
        v-if="EnrollmentRules.canStart(enrollment)"
        size="sm"
        :variant="EnrollmentRules.isRerun(enrollment) ? 'outline' : 'solid'"
        :tone="EnrollmentRules.isRerun(enrollment) ? 'neutral' : 'primary'"
        class="ms-auto max-md:min-h-11"
        :is-disabled="Boolean(blocked)"
        :title="blocked ?? undefined"
        @click="enrolling = true"
      >
        <template #leading
          ><RotateCcw v-if="EnrollmentRules.isRerun(enrollment)" :size="14" /><Rocket v-else :size="14"
        /></template>
        {{ EnrollmentRules.isRerun(enrollment) ? "Rerun" : "Enroll" }}
      </Button>
    </div>
    <p v-if="blocked && EnrollmentRules.canStart(enrollment)" class="text-xs text-muted-foreground">{{ blocked }}</p>
    <Alert
      v-if="enrollment?.reason && EnrollmentRules.isRerun(enrollment)"
      :severity="status === InfraJobStatus.Interrupted ? 'warning' : 'danger'"
      :description="enrollment.reason"
    />
    <OneTimeKeyBlock
      v-if="enrollment?.oneTimeKey && !succeeded"
      :one-time-key="enrollment.oneTimeKey"
      :login-user="loginUser"
      :address="address"
    />
    <JobSteps
      v-if="enrollment?.steps.length && !succeeded"
      :steps="enrollment.steps"
      :label="`${props.server.name} enrollment steps`"
    />
    <template v-if="succeeded">
      <dl class="grid gap-x-4 gap-y-2 text-sm sm:grid-cols-2">
        <div v-if="facts.hostKeyFingerprint" class="min-w-0 sm:col-span-2">
          <dt class="text-xs text-muted-foreground">Host key</dt>
          <dd class="font-mono text-xs break-all">
            {{ facts.hostKeyFingerprint
            }}<span v-if="EnrollmentRules.confirmedBy(facts)" class="ms-2 font-sans text-muted-foreground">{{
              EnrollmentRules.confirmedBy(facts)
            }}</span>
          </dd>
        </div>
        <div v-if="facts.tailnetAddress" class="min-w-0">
          <dt class="text-xs text-muted-foreground">Tailnet address</dt>
          <dd class="font-mono text-xs">{{ facts.tailnetAddress }}</dd>
        </div>
        <div v-if="facts.publicAddress" class="min-w-0">
          <dt class="text-xs text-muted-foreground">Public address</dt>
          <dd class="font-mono text-xs">{{ facts.publicAddress }}</dd>
        </div>
        <div v-if="facts.nodeName || facts.nodeId" class="min-w-0">
          <dt class="text-xs text-muted-foreground">Node</dt>
          <dd class="break-all">{{ facts.nodeName ?? facts.nodeId }}</dd>
        </div>
        <div v-if="facts.kitDigest" class="min-w-0">
          <dt class="text-xs text-muted-foreground">Kit</dt>
          <dd class="font-mono text-xs" :title="facts.kitDigest">{{ InfraJobRules.shortDigest(facts.kitDigest) }}</dd>
        </div>
        <div v-if="facts.publicSshClosed !== undefined" class="min-w-0">
          <dt class="text-xs text-muted-foreground">Public SSH</dt>
          <dd>{{ facts.publicSshClosed ? "Closed" : "Open" }}</dd>
        </div>
      </dl>
      <MintedKeys v-if="enrollment?.mints.length" :mints="enrollment.mints" />
      <details v-if="enrollment?.steps.length" class="text-sm">
        <summary class="cursor-pointer text-xs font-medium text-muted-foreground max-md:py-2">
          {{ enrollment.steps.length }} {{ enrollment.steps.length === 1 ? "step" : "steps" }}
        </summary>
        <JobSteps class="mt-2" :steps="enrollment.steps" :label="`${props.server.name} enrollment steps`" />
      </details>
    </template>
    <EnrollmentModal
      :open="enrolling"
      :server="props.server"
      :enrollment="enrollment"
      :blocked="blocked"
      :operations="operations"
      @update:open="enrolling = $event"
    />
  </section>
  <p
    v-else-if="props.server.controlHost"
    :aria-label="`${props.server.name} control host`"
    class="rounded-xl border border-border bg-background/40 p-3 text-sm"
  >
    <span class="font-medium">Control host.</span>
    <span class="text-muted-foreground">
      The consoles run here and reach it at its private address; enrollment is for fresh public servers.</span
    >
  </p>
</template>
