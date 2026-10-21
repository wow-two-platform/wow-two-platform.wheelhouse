<script setup lang="ts">
import { ReleaseSets } from "@/domain/deployments";
import { computed, ref } from "vue";
import { RouterLink, useRoute } from "vue-router";
import { ArrowRight, Crosshair, ExternalLink, FileText, RotateCcw, TriangleAlert, Undo2, Wrench } from "lucide-vue-next";

import { Button } from "@wow-two-beta/fe/presentation/actions";
import { Badge, DescriptionGroup, RelativeTime } from "@wow-two-beta/fe/presentation/display";
import { Alert, DataRegion } from "@wow-two-beta/fe/presentation/feedback";
import { PageHeaderActions } from "@wow-two-beta/fe/presentation/layout";

import { useDeploymentDetail, useDeploymentOutcome, useDeploymentTargets, useTargetState } from "@/application/deployments";
import { ConsolePaths, productLocation } from "@/application/navigation";
import { useProducts } from "@/application/products";
import { useRefresh } from "@/bootstrap/query";
import { Attention, ChangeReasonWords, DeployState, Measures, ServiceResult, ServiceResultTones, ServiceResultWords } from "@/domain/common";
import { DeploymentOutcome, DeploymentStates, ServiceRollouts, TargetCondition } from "@/domain/deployments";
import { InfoPopover, Panel, RefreshButton, ServiceResultIcons } from "@/presentation/common/components";
import { ProductIcon } from "@/presentation/products";

import DeployModal from "../components/DeployModal.vue";
import DeploymentStateChip from "../components/DeploymentStateChip.vue";
import DeploymentSteps from "../components/DeploymentSteps.vue";
import ImpactList from "../components/ImpactList.vue";
import ReconcileModal from "../components/ReconcileModal.vue";
import ServiceLogsModal from "../components/ServiceLogsModal.vue";
import { useDeployModal } from "../hooks/useDeployModal";
import { EnvironmentSettingsRules } from "@/domain/settings";

/**
 * One deployment on its own page, in the order the status design reads it: the state chip, then what the environment
 * runs now with each service's glyph, then the cause and what else it likely hurt, the actions that fit the state
 * (Partial and Failed reconcile, Rolled back redeploys, Not applied retries), the rollout steps split where changes
 * start, and each service from and to with how it ended and how it is now. Beside them, the impact as a list with the
 * service map one tap away, the sites, the snapshot and the links. A live rollout's steps follow the target every three
 * seconds; a finished one reads only the control plane's records.
 */
defineOptions({ name: "DeploymentDetailPage" });
const route = useRoute();
const id = computed(() => {
  const value = route.params["id"];
  const text = Array.isArray(value) ? value[0] : value;
  return typeof text === "string" && /^[0-9a-f-]{36}$/i.test(text) ? text : null;
});
const detail = useDeploymentDetail(id);
const deployment = computed(() => detail.data.value?.deployment ?? null);
/** The settings the deploy changed, named as the likely cause when it then failed; null when it changed none. */
const settingsHint = computed(() =>
  EnvironmentSettingsRules.hint(deployment.value?.settingsChanged, deployment.value?.settingsSuspect ?? false),
);
const isLive = computed(() => deployment.value?.outcome === DeploymentOutcome.Pending);
const outcome = useDeploymentOutcome(
  () => (isLive.value ? id.value : null),
  () => deployment.value?.status ?? null,
);
const products = useProducts();
const targets = useDeploymentTargets();
const state = useTargetState(() => deployment.value?.targetId ?? null);
const deploy = useDeployModal();
const reconciling = ref(false);
const logsService = ref<string | null>(null);

const product = computed(() => products.products.value.find((item) => item.slug === deployment.value?.product) ?? null);
const productName = computed(() => product.value?.name ?? deployment.value?.product ?? "Unknown product");
/** The target still exists, so a redeploy or roll back can go to it. */
const targetExists = computed(() => Boolean(targets.data.value?.some((item) => item.id === deployment.value?.targetId)));
/** A live rollout's steps as the target reports them now; a finished one's from the record. */
const steps = computed(() => (isLive.value && outcome.data.value?.steps?.length ? outcome.data.value.steps : (detail.data.value?.steps ?? [])));
const status = computed(() => (isLive.value ? (outcome.data.value?.status ?? deployment.value?.status) : deployment.value?.status));
/** The deployment in the taxonomy; a live one that finished since the read reads its new status until the next read. */
const reading = computed(() => {
  const item = deployment.value;
  if (!item || !status.value) return null;
  return DeploymentStates.of(status.value === item.status ? item : { status: status.value, reason: item.reason });
});
const services = computed(() => detail.data.value?.services ?? null);
/** The release a roll back restored: the target's previous success. */
const restored = computed(() => detail.data.value?.previous?.release ?? null);
/** "prod now runs two releases": what the environment runs because of this deployment. */
const nowRuns = computed(() =>
  reading.value && deployment.value
    ? DeploymentStates.nowRuns(reading.value, deployment.value.environment, deployment.value.release, restored.value)
    : "",
);
/** Each service with its versions and how it ended: the API's per-service results first, in rollout order. */
const serviceRows = computed(() => {
  // A deploy that changed nothing touched no service, whatever its release would have changed.
  if (reading.value?.state === DeployState.NotApplied) return [];
  const results = deployment.value?.services ?? [];
  const changes = services.value ?? [];
  const names = [...results.map((item) => item.service), ...changes.map((item) => item.service).filter((name) => !results.some((item) => item.service === name))];
  return names.map((name) => {
    const result = results.find((item) => item.service === name) ?? null;
    const change = changes.find((item) => item.service === name) ?? null;
    return {
      service: name,
      from: result?.from ?? change?.from ?? null,
      to: result?.to ?? change?.to ?? null,
      changed: change?.changed ?? (result ? result.result !== ServiceResult.Unchanged : false),
      result,
      legacy: result ? null : ServiceRollouts.outcome(detail.data.value?.serviceRollout, name),
    };
  });
});
const impact = computed(() => DeploymentStates.impact(deployment.value?.services, deployment.value?.effects ?? []));
/** The service map on the product's page, opened on the origin in this deployment's environment. */
const mapLocation = computed(() => {
  const item = deployment.value;
  if (!item?.product) return null;
  const origin = item.services?.find((service) => service.origin)?.service ?? item.cause?.origin ?? null;
  return productLocation(item.product, { env: item.environment, ...(origin ? { inspect: "service", item: origin } : {}) });
});
/** The services the roll back restored, and those it could not. */
const rollback = computed(() => {
  const results = deployment.value?.services ?? [];
  const back = results.filter((item) => item.result === ServiceResult.Restored).map((item) => item.service);
  const stuck = results.filter((item) => item.result === ServiceResult.Failed).map((item) => item.service);
  return back.length ? { back, stuck } : null;
});
/** The cause card's tone: red when it needs action, amber when it is worth a look, quiet otherwise. */
const causeTone = computed(() =>
  reading.value?.attention === Attention.Act
    ? "border-destructive/40 bg-destructive-soft/60"
    : reading.value?.attention === Attention.Look
      ? "border-warning/45 bg-warning-soft/60"
      : "border-border bg-muted/40",
);
const hasCauseSection = computed(
  () =>
    Boolean(reading.value?.cause?.message) &&
    reading.value?.state !== DeployState.InProgress &&
    reading.value?.state !== DeployState.Succeeded,
);
/** The action that fits the state: reconcile, redeploy, retry. */
const fitting = computed(() => (reading.value ? DeploymentStates.action(reading.value.state) : null));
const logServices = computed(() => serviceRows.value.filter((item) => item.to !== null).map((item) => item.service));
const needsReconciliation = computed(
  () => state.data.value?.condition === TargetCondition.NeedsReconciliation && state.data.value.active !== null,
);
const facts = computed(() => {
  const item = deployment.value;
  if (!item) return [];
  return [
    { label: "Target", value: item.targetId ?? "—" },
    { label: "Previous success", value: ReleaseSets.text(detail.data.value?.previous?.release) || "—" },
    { label: "Actor", value: item.actor ?? "—" },
    ...(item.deployReason ? [{ label: "Reason", value: item.deployReason }] : []),
    { label: "Submitted", value: Measures.moment(item.submittedAt) },
    { label: "Duration", value: item.durationSeconds !== null ? Measures.duration(item.durationSeconds) : isLive.value ? "running" : "—" },
    // A release set names each repository's release and commit (decision 70); one repository has one commit.
    ...(item.components?.length
      ? item.components.map((component) => ({
          label: component.name ?? component.repository ?? "Repository",
          value: [component.release, component.sourceCommit?.slice(0, 12)].filter(Boolean).join(" · ") || "—",
        }))
      : [{ label: "Commit", value: item.sourceCommit ? item.sourceCommit.slice(0, 12) : "—" }]),
    { label: "Kind", value: item.kind === "candidate" ? `commit build${item.branch ? ` · ${item.branch}` : ""}` : (item.kind ?? "—") },
  ];
});
const refresh = useRefresh(() =>
  Promise.all([detail.refetch(), state.refetch(), ...(isLive.value ? [outcome.refetch()] : [])]),
);

/** A glyph's colour by its result's tone. @internal */
const ToneText: Readonly<Record<string, string>> = {
  calm: "text-success",
  info: "text-info",
  warning: "text-warning",
  danger: "text-destructive",
  quiet: "text-muted-foreground",
  empty: "text-subtle-foreground",
};

function redeploy(): void {
  const item = deployment.value;
  if (item?.targetId && item.bundleId) deploy.openDeploy({ target: item.targetId, release: item.bundleId });
}

function rollBack(): void {
  const item = deployment.value;
  const previous = detail.data.value?.rollback;
  if (item?.targetId && previous?.bundleId) deploy.openDeploy({ target: item.targetId, release: previous.bundleId });
}
</script>

<template>
  <div class="flex min-w-0 flex-col gap-6">
    <PageHeaderActions>
      <RefreshButton size="md" icon-only :refreshing="refresh.refreshing.value" @refresh="refresh.refresh" />
      <Button
        v-if="deployment?.bundleId && targetExists"
        :variant="fitting === 'redeploy' || fitting === 'retry' ? 'solid' : 'outline'"
        :tone="fitting === 'redeploy' || fitting === 'retry' ? 'primary' : 'neutral'"
        :aria-label="`${fitting === 'retry' ? 'Retry' : 'Redeploy'} ${deployment.release ?? deployment.bundleId}`"
        @click="redeploy"
        ><template #leading><RotateCcw :size="16" /></template
        ><span class="hidden sm:inline">{{ fitting === "retry" ? "Retry" : "Redeploy" }}</span></Button
      >
      <Button
        v-if="detail.data.value?.rollback && targetExists"
        variant="outline"
        tone="neutral"
        :aria-label="`Roll back to ${detail.data.value.rollback.release ?? 'the previous release'}`"
        @click="rollBack"
        ><template #leading><Undo2 :size="16" /></template><span class="hidden sm:inline">Roll back</span></Button
      >
      <Button
        v-if="logServices.length && deployment?.targetId && targetExists"
        variant="outline"
        tone="neutral"
        :aria-label="`Read ${logServices[0]} logs`"
        @click="logsService = logServices[0] ?? null"
        ><template #leading><FileText :size="16" /></template><span class="hidden sm:inline">Read logs</span></Button
      >
      <Button v-if="needsReconciliation" variant="soft" tone="danger" @click="reconciling = true"
        ><template #leading><Wrench :size="16" /></template>Reconcile</Button
      >
    </PageHeaderActions>
    <DataRegion
      :is-first-load="detail.isFirstLoad.value"
      :is-refreshing="detail.isRefreshing.value"
      :error="detail.error.value"
      :has-data="Boolean(detail.data.value)"
      :is-empty="id === null"
      empty-title="This is not a deployment address"
      empty-description="Open a deployment from the Deployments page."
      loading-label="Loading the deployment"
      @retry="detail.refetch()"
    >
      <div class="grid min-w-0 items-start gap-6 lg:grid-cols-[minmax(0,1fr)_320px]">
        <div class="flex min-w-0 flex-col gap-6">
          <Panel :title="ReleaseSets.text(deployment?.release) || deployment?.bundleId || 'Deployment'">
            <template #title>
              <span class="flex min-w-0 flex-wrap items-center gap-3">
                <ProductIcon :product-slug="deployment?.product" :name="productName" size="md" />
                <span class="min-w-0">
                  <RouterLink
                    v-if="deployment?.product"
                    :to="ConsolePaths.product(deployment.product)"
                    class="block text-sm font-normal text-muted-foreground hover:text-primary"
                    >{{ productName }}</RouterLink
                  >
                  <span class="flex flex-wrap items-center gap-2">
                    <span class="font-mono text-base font-semibold">{{ ReleaseSets.text(deployment?.release) || deployment?.bundleId || "—" }}</span>
                    <Badge :variant="deployment?.environment === 'prod' ? 'info' : 'neutral'">{{
                      deployment?.environment ?? deployment?.targetId ?? "—"
                    }}</Badge>
                    <Badge v-if="deployment?.kind === 'candidate'" variant="neutral">commit build</Badge>
                  </span>
                </span>
              </span>
            </template>
            <div class="flex min-w-0 flex-col gap-4">
              <div class="flex flex-wrap items-center gap-x-3 gap-y-1.5">
                <DeploymentStateChip v-if="reading" :reading="reading" />
                <span v-if="deployment" class="text-sm text-muted-foreground">
                  <template v-if="reading?.state === DeployState.NotApplied"
                    >stopped before any change {{ Measures.moment(deployment.completedAt ?? deployment.submittedAt) }}</template
                  ><template v-else-if="deployment.startedAt"
                    >changes started {{ Measures.moment(deployment.startedAt)
                    }}<template v-if="deployment.completedAt"> · stopped {{ Measures.moment(deployment.completedAt) }}</template></template
                  ><template v-else>submitted <RelativeTime :value="deployment.submittedAt" /></template>
                  by {{ deployment.actor ?? "someone" }}
                </span>
              </div>
              <div
                v-if="reading"
                :class="[
                  'flex flex-wrap items-center gap-x-3 gap-y-2 rounded-xl border px-3 py-2.5 text-sm',
                  reading.attention === Attention.Act
                    ? 'border-destructive/30 bg-destructive-soft/50'
                    : reading.attention === Attention.Look
                      ? 'border-warning/35 bg-warning-soft/50'
                      : 'border-border bg-muted/40',
                ]"
                aria-label="What the environment runs now"
              >
                <span class="font-semibold">{{ nowRuns }}</span>
                <ul v-if="deployment?.services?.length" class="flex flex-wrap items-center gap-x-3 gap-y-1">
                  <li v-for="service in deployment.services" :key="service.service" class="inline-flex items-center gap-1.5 text-xs">
                    <span
                      :class="[
                        'inline-flex size-5 items-center justify-center rounded-full',
                        ToneText[ServiceResultTones[service.result]],
                        service.origin ? 'ring-2 ring-destructive/60' : '',
                      ]"
                      ><component :is="ServiceResultIcons[service.result]" :size="14" aria-hidden="true"
                    /></span>
                    <span class="font-mono">{{ service.service }}</span>
                    <span v-if="service.result === ServiceResult.Restored ? service.from : service.to" class="font-mono text-muted-foreground">{{
                      service.result === ServiceResult.Restored ? service.from : service.to
                    }}</span>
                    <span class="text-muted-foreground">{{ ServiceResultWords[service.result] }}</span>
                  </li>
                </ul>
              </div>
              <p v-if="deployment?.watch && reading?.state === DeployState.Succeeded" class="text-sm text-muted-foreground">
                {{
                  deployment.watch.status === "watching"
                    ? "Watching its containers and sites once a minute until the window closes."
                    : deployment.watch.status === "passed"
                      ? `Watched after the deploy: ${deployment.watch.checks ?? 0} looks found nothing wrong.`
                      : deployment.watch.status === "replaced"
                        ? "Another rollout replaced it during its watch."
                        : "Its watch could not read the target."
                }}
              </p>
              <Alert
                v-if="settingsHint"
                :severity="deployment?.settingsSuspect ? 'warning' : 'info'"
                :title="deployment?.settingsSuspect ? 'It changed settings, then failed' : 'It changed settings'"
                :description="settingsHint"
              />
              <Alert
                v-if="needsReconciliation"
                severity="warning"
                title="The target needs reconciliation"
                description="Its last rollout stopped part-way. Inspect the target, then reconcile to accept new deployments."
              />
              <DescriptionGroup layout="stacked" density="md" class="grid grid-cols-2 gap-x-6 sm:grid-cols-4 [&_dt]:text-xs" :items="facts" />
            </div>
          </Panel>
          <!-- On a phone the impact list stands in for the service map, right under what the environment runs. -->
          <Panel v-if="impact.length" title="Impact" class="lg:hidden">
            <ImpactList :lines="impact" :map="mapLocation" />
          </Panel>
          <Panel v-if="hasCauseSection" title="Cause and effects">
            <template #info>
              <InfoPopover title="Cause and effects">
                <p>
                  The origin is the earliest failing signal: a step that names the service, the first health failure, or a
                  platform need down. An effect is a service that turned unhealthy after the origin and declares a path to
                  it (its startup order or a shared platform need) — inferred from the declaration, never an observed call.
                  Without a declared path, each failing service is an origin of its own.
                </p>
              </InfoPopover>
            </template>
            <ul class="flex flex-col gap-3">
              <li :class="['flex items-start gap-3 rounded-xl border p-3', causeTone]">
                <Crosshair :size="18" aria-hidden="true" class="mt-0.5 shrink-0 text-destructive-soft-foreground" />
                <span class="min-w-0">
                  <span class="block font-semibold"
                    >Origin · {{ reading?.cause?.origin ?? reading?.cause?.kind ?? "the rollout" }}</span
                  >
                  <span class="block text-sm break-words text-muted-foreground">{{ DeploymentStates.causeText(reading?.cause ?? null) }}</span>
                  <span v-if="deployment?.watch?.findings.length && reading?.substate !== 'at_deploy'" class="mt-1 block text-xs text-muted-foreground">{{
                    deployment.watch.findings.join("; ")
                  }}</span>
                </span>
              </li>
              <li
                v-for="effect in deployment?.effects ?? []"
                :key="effect.service"
                class="flex items-start gap-3 rounded-xl border border-warning/45 bg-warning-soft/60 p-3"
              >
                <TriangleAlert :size="18" aria-hidden="true" class="mt-0.5 shrink-0 text-warning-soft-foreground" />
                <span class="min-w-0">
                  <span class="block font-semibold">Effect · {{ effect.service }} {{ effect.finding }}</span>
                  <span class="block text-sm text-muted-foreground"
                    >Likely via {{ effect.via }} — a declared path, not an observed call.</span
                  >
                </span>
              </li>
              <li v-if="rollback" :class="['flex items-start gap-3 rounded-xl border p-3', rollback.stuck.length ? 'border-destructive/40 bg-destructive-soft/60' : 'border-warning/45 bg-warning-soft/60']">
                <Undo2 :size="18" aria-hidden="true" class="mt-0.5 shrink-0" />
                <span class="min-w-0">
                  <span class="block font-semibold"
                    >Roll back · {{ rollback.back.join(", ") }} restored<template v-if="rollback.stuck.length"
                      >, {{ rollback.stuck.join(", ") }} could not be</template
                    ></span
                  >
                  <span class="block text-sm text-muted-foreground">{{
                    rollback.stuck.length
                      ? "Reconcile the target, then deploy a fix forward or restore the snapshot."
                      : `Back on ${restored ?? "the previous release"}.`
                  }}</span>
                </span>
              </li>
              <li
                v-for="warning in reading?.substate === 'at_deploy' ? (deployment?.warnings.slice(1) ?? []) : []"
                :key="warning"
                class="flex items-start gap-3 rounded-xl border border-warning/45 bg-warning-soft/60 p-3 text-sm"
              >
                <TriangleAlert :size="18" aria-hidden="true" class="mt-0.5 shrink-0 text-warning-soft-foreground" />{{ warning }}
              </li>
            </ul>
          </Panel>
          <Panel title="Rollout steps" v-bind="isLive ? { description: 'Following the target live.' } : {}">
            <DeploymentSteps v-if="steps.length" :steps="steps" is-sectioned />
            <p v-else class="text-sm text-muted-foreground">
              {{
                detail.data.value?.observed === false
                  ? reading?.state === DeployState.NotApplied
                    ? "It never reached the target, so the target recorded no steps."
                    : "The target's report of this rollout is not on this control plane."
                  : isLive
                    ? "Waiting for the target to start the rollout."
                    : "The target recorded no steps: the runner refused the release before it started."
              }}
            </p>
          </Panel>
          <Panel title="Services">
            <template #info>
              <InfoPopover title="Services">
                <p>
                  Each service against {{ ReleaseSets.text(detail.data.value?.previous?.release) || "what the target ran before" }}: its
                  versions from and to (and back, when restored), how it ended in this deploy, and how the target last
                  reported it. The ringed result is where the trouble started.
                </p>
              </InfoPopover>
            </template>
            <p v-if="!serviceRows.length" class="text-sm text-muted-foreground">
              {{
                reading?.state === DeployState.NotApplied
                  ? "It changed no service."
                  : "This control plane no longer holds the release's bundle, so its services are unknown."
              }}
            </p>
            <ul v-else class="flex flex-col divide-y divide-border" aria-label="Services">
              <li
                v-for="row in serviceRows"
                :key="row.service"
                class="grid grid-cols-1 items-center gap-x-4 gap-y-1.5 py-2.5 md:grid-cols-[minmax(6rem,0.8fr)_minmax(12rem,1.4fr)_minmax(9rem,1.4fr)_minmax(5rem,0.8fr)_auto]"
              >
                <span class="font-mono text-sm font-medium">{{ row.service }}</span>
                <span class="flex flex-wrap items-center gap-1.5 font-mono text-xs">
                  <template v-if="!row.changed">{{ row.from ?? row.to ?? "—" }} <span class="font-sans text-muted-foreground">(unchanged)</span></template>
                  <template v-else>
                    <span :class="row.result?.result === ServiceResult.Restored ? '' : 'text-muted-foreground line-through decoration-muted-foreground/50'">{{
                      row.from ?? "new"
                    }}</span>
                    <ArrowRight :size="12" aria-label="to" />
                    <span class="font-medium">{{ row.to ?? "removed" }}</span>
                    <template v-if="row.result?.result === ServiceResult.Restored"
                      ><ArrowRight :size="12" aria-label="back to" /><span class="font-medium">{{ row.from ?? "—" }}</span></template
                    >
                  </template>
                </span>
                <span class="flex min-w-0 items-center gap-1.5 text-sm">
                  <template v-if="row.result">
                    <span
                      :class="[
                        'inline-flex size-5 shrink-0 items-center justify-center rounded-full',
                        ToneText[ServiceResultTones[row.result.result]],
                        row.result.origin ? 'ring-2 ring-destructive/60' : '',
                      ]"
                      ><component :is="ServiceResultIcons[row.result.result]" :size="14" aria-hidden="true"
                    /></span>
                    <span>{{ ServiceResultWords[row.result.result] }}<template v-if="row.result.origin"> · origin</template></span>
                    <span v-if="row.result.reason" class="text-xs text-muted-foreground"
                      >· {{ ChangeReasonWords[row.result.reason] ?? row.result.reason }}</span
                    >
                  </template>
                  <Badge v-else-if="row.legacy" :variant="row.legacy.tone">{{ row.legacy.label }}</Badge>
                  <span v-else class="text-muted-foreground">{{ row.changed ? "changed" : "kept running" }}</span>
                </span>
                <span class="text-xs text-muted-foreground">{{ row.result?.health ?? "—" }}</span>
                <Button
                  v-if="row.to !== null && deployment?.targetId && targetExists"
                  variant="ghost"
                  tone="neutral"
                  size="sm"
                  class="min-h-11 justify-self-start md:min-h-8"
                  :aria-label="`Read ${row.service} logs`"
                  @click="logsService = row.service"
                  ><template #leading><FileText :size="14" /></template><span class="hidden sm:inline">Logs</span></Button
                >
              </li>
            </ul>
          </Panel>
        </div>
        <div class="flex min-w-0 flex-col gap-6">
          <Panel v-if="impact.length" title="Impact" class="max-lg:hidden">
            <template #info>
              <InfoPopover title="Impact">
                <p>
                  Where the trouble started, then the services it likely hurt along a declared path, then any other service
                  not fine. Open the map to see them on the product's service map.
                </p>
              </InfoPopover>
            </template>
            <ImpactList :lines="impact" :map="mapLocation" />
          </Panel>
          <Panel title="Sites">
            <template #info>
              <InfoPopover title="Sites">
                <p>
                  Each site this rollout published, probed once through the ingress right after the services turned
                  healthy. A site that did not answer then says why; it may answer now.
                </p>
              </InfoPopover>
            </template>
            <ul v-if="detail.data.value?.sites.length" class="flex flex-col gap-3">
              <li v-for="site in detail.data.value.sites" :key="site.name + site.url" class="min-w-0 text-sm">
                <span class="flex items-center justify-between gap-2">
                  <span class="font-medium">{{ site.name }}</span>
                  <Badge v-if="site.probe" :variant="site.probe.ok ? 'success' : 'warning'">{{
                    site.probe.ok ? `answered${site.probe.status ? ` ${site.probe.status}` : ""}` : "no answer"
                  }}</Badge>
                  <Badge v-else variant="neutral">not probed</Badge>
                </span>
                <a
                  :href="site.url"
                  target="_blank"
                  rel="noreferrer noopener"
                  class="mt-1 flex min-w-0 items-center gap-1 font-mono text-xs text-muted-foreground hover:text-primary"
                  ><span class="truncate">{{ site.url }}</span><ExternalLink :size="12" class="shrink-0" aria-hidden="true"
                /></a>
                <span v-if="site.probe && !site.probe.ok && site.probe.detail" class="mt-1 flex items-start gap-1 text-xs text-warning"
                  ><TriangleAlert :size="12" class="mt-0.5 shrink-0" aria-hidden="true" />{{ site.probe.detail }}</span
                >
              </li>
            </ul>
            <p v-else class="text-sm text-muted-foreground">
              {{ isLive ? "Sites are published and probed once the services are healthy." : "No sites were published by this rollout." }}
            </p>
          </Panel>
          <Panel v-if="detail.data.value?.snapshot" title="Snapshot">
            <template #info>
              <InfoPopover title="Snapshot">
                <p>
                  The databases and volumes a prod rollout saves on the target before it changes anything, with their
                  sizes. Restoring is never automatic: run the restore commands on the target, in order, after
                  inspecting what failed.
                </p>
              </InfoPopover>
            </template>
            <div class="flex flex-col gap-3 text-sm">
              <p class="break-all font-mono text-xs">{{ detail.data.value.snapshot.path }}</p>
              <ul class="flex flex-col gap-1 text-xs">
                <li v-for="item in detail.data.value.snapshot.databases" :key="item.file" class="flex justify-between gap-2">
                  <span class="min-w-0 truncate">{{ item.database }} <span class="text-muted-foreground">on {{ item.host }}</span></span>
                  <span class="shrink-0 tabular-nums text-muted-foreground">{{ item.bytes !== null ? Measures.bytes(item.bytes) : "—" }}</span>
                </li>
                <li v-for="item in detail.data.value.snapshot.volumes" :key="item.file" class="flex justify-between gap-2">
                  <span class="min-w-0 truncate">{{ item.volume }} <span class="text-muted-foreground">volume</span></span>
                  <span class="shrink-0 tabular-nums text-muted-foreground">{{ item.bytes !== null ? Measures.bytes(item.bytes) : "—" }}</span>
                </li>
              </ul>
              <details v-if="detail.data.value.snapshot.restore.length">
                <summary class="cursor-pointer text-xs font-medium">Restore commands</summary>
                <p class="mt-2 text-xs text-muted-foreground">
                  On the target, in order; export PGPASSWORD first for a database. Restore only after inspecting what failed.
                </p>
                <pre
                  v-for="command in detail.data.value.snapshot.restore"
                  :key="command"
                  class="mt-2 overflow-x-auto whitespace-pre-wrap break-all rounded-md bg-muted p-2 font-mono text-xs"
                >{{ command }}</pre>
              </details>
            </div>
          </Panel>
          <Panel title="Links">
            <ul class="flex flex-col gap-2 text-sm">
              <li v-if="deployment?.product">
                <RouterLink :to="ConsolePaths.product(deployment.product)" class="hover:text-primary">{{ productName }} overview</RouterLink>
              </li>
              <li v-if="deployment?.product">
                <RouterLink :to="ConsolePaths.product(deployment.product, 'deployments')" class="hover:text-primary"
                  >{{ productName }} deployments</RouterLink
                >
              </li>
              <li v-if="detail.data.value?.previous">
                <RouterLink :to="`${ConsolePaths.deployments}/${detail.data.value.previous.id}`" class="hover:text-primary"
                  >Previous success on this target · {{ ReleaseSets.text(detail.data.value.previous.release) }}</RouterLink
                >
              </li>
              <li>
                <RouterLink :to="ConsolePaths.deployments" class="hover:text-primary">All deployments</RouterLink>
              </li>
            </ul>
          </Panel>
        </div>
      </div>
    </DataRegion>
    <ServiceLogsModal
      :open="logsService !== null"
      :target="deployment?.targetId ?? null"
      :service="logsService"
      @update:open="(open) => !open && (logsService = null)"
    />
    <ReconcileModal
      v-if="deployment?.targetId && state.data.value?.active"
      :target="deployment.targetId"
      :active="state.data.value.active"
      :open="reconciling"
      @update:open="reconciling = $event"
    />
    <DeployModal
      :key="deploy.session.value"
      :open="deploy.open.value"
      :selection="deploy.selection.value"
      @update:open="deploy.onOpenChange"
    />
  </div>
</template>
