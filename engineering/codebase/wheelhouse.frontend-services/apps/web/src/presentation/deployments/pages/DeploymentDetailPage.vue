<script setup lang="ts">
import { computed, ref } from "vue";
import { RouterLink, useRoute } from "vue-router";
import { AlertTriangle, ArrowRight, ExternalLink, FileText, RotateCcw, Undo2, Wrench } from "lucide-vue-next";

import { Button } from "@wow-two-beta/fe/presentation/actions";
import { Badge, DescriptionGroup, RelativeTime } from "@wow-two-beta/fe/presentation/display";
import { Alert, DataRegion } from "@wow-two-beta/fe/presentation/feedback";
import { PageHeaderActions } from "@wow-two-beta/fe/presentation/layout";

import { useDeploymentDetail, useDeploymentOutcome, useDeploymentTargets, useTargetState } from "@/application/deployments";
import { ConsolePaths } from "@/application/navigation";
import { useProducts } from "@/application/products";
import { useRefresh } from "@/bootstrap/query";
import { Measures } from "@/domain/common";
import { DeploymentExtensions, DeploymentOutcome, TargetCondition } from "@/domain/deployments";
import { Panel, RefreshButton } from "@/presentation/common/components";
import { ProductIcon } from "@/presentation/products";

import DeployModal from "../components/DeployModal.vue";
import DeploymentSteps from "../components/DeploymentSteps.vue";
import JobStatusIndicator from "../components/JobStatusIndicator.vue";
import ReconcileModal from "../components/ReconcileModal.vue";
import ServiceLogsModal from "../components/ServiceLogsModal.vue";
import { useDeployModal } from "../hooks/useDeployModal";

/**
 * One deployment on its own page: what went where and how, its rollout steps, each service from and to, the sites it
 * published and what needs a look; with redeploy, roll back, reconcile and log actions. A live rollout's steps follow
 * the target every three seconds; a finished one reads only the control plane's records.
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
const services = computed(() => detail.data.value?.services ?? null);
const logServices = computed(() => (services.value ?? []).filter((item) => item.to !== null).map((item) => item.service));
const needsReconciliation = computed(
  () => state.data.value?.condition === TargetCondition.NeedsReconciliation && state.data.value.active !== null,
);
const facts = computed(() => {
  const item = deployment.value;
  if (!item) return [];
  return [
    { label: "Target", value: item.targetId ?? "—" },
    { label: "Actor", value: item.actor ?? "—" },
    { label: "Submitted", value: Measures.moment(item.submittedAt) },
    { label: "Started", value: Measures.moment(item.startedAt) },
    { label: "Completed", value: Measures.moment(item.completedAt) },
    { label: "Duration", value: item.durationSeconds !== null ? Measures.duration(item.durationSeconds) : isLive.value ? "running" : "—" },
    { label: "Commit", value: item.sourceCommit ? item.sourceCommit.slice(0, 12) : "—" },
    { label: "Kind", value: item.kind === "candidate" ? `commit build${item.branch ? ` · ${item.branch}` : ""}` : (item.kind ?? "—") },
  ];
});
const refresh = useRefresh(() =>
  Promise.all([detail.refetch(), state.refetch(), ...(isLive.value ? [outcome.refetch()] : [])]),
);

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
        variant="outline"
        tone="neutral"
        :aria-label="`Redeploy ${deployment.release ?? deployment.bundleId}`"
        @click="redeploy"
        ><template #leading><RotateCcw :size="16" /></template><span class="hidden sm:inline">Redeploy</span></Button
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
          <Panel :title="deployment?.release ?? deployment?.bundleId ?? 'Deployment'">
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
                    <span class="font-mono text-base font-semibold">{{ deployment?.release ?? deployment?.bundleId ?? "—" }}</span>
                    <Badge :variant="deployment?.environment === 'prod' ? 'info' : 'neutral'">{{
                      deployment?.environment ?? deployment?.targetId ?? "—"
                    }}</Badge>
                    <Badge v-if="deployment?.kind === 'candidate'" variant="neutral">commit build</Badge>
                  </span>
                </span>
              </span>
            </template>
            <div class="flex min-w-0 flex-col gap-4">
              <div class="flex flex-wrap items-center gap-3">
                <JobStatusIndicator v-if="status" :status="status" />
                <span v-if="deployment" class="text-sm text-muted-foreground">
                  submitted <RelativeTime :value="deployment.submittedAt" /> by {{ deployment.actor ?? "someone" }}
                </span>
              </div>
              <Alert
                v-if="deployment?.reason && deployment.outcome !== DeploymentOutcome.Succeeded"
                severity="danger"
                :title="DeploymentExtensions.label(deployment.status)"
                :description="deployment.reason"
              />
              <Alert
                v-for="warning in deployment?.warnings ?? []"
                :key="warning"
                severity="warning"
                title="Needs a look"
                :description="warning"
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
          <Panel title="Rollout steps" :description="isLive ? 'Following the target live.' : 'As the target recorded them.'">
            <DeploymentSteps v-if="steps.length" :steps="steps" />
            <p v-else class="text-sm text-muted-foreground">
              {{
                detail.data.value?.observed === false
                  ? "The target's report of this rollout is not on this control plane."
                  : isLive
                    ? "Waiting for the target to start the rollout."
                    : "The target recorded no steps: the runner refused the release before it started."
              }}
            </p>
          </Panel>
          <Panel
            title="Services"
            :description="detail.data.value?.previous ? `Against ${detail.data.value.previous.release ?? 'the previous success'} on this target.` : 'The first rollout this control plane saw on this target.'"
          >
            <p v-if="services === null" class="text-sm text-muted-foreground">
              This control plane no longer holds the release's bundle, so its services are unknown.
            </p>
            <ul v-else class="flex flex-col divide-y divide-border">
              <li v-for="service in services" :key="service.service" class="flex flex-wrap items-center justify-between gap-3 py-2.5">
                <span class="flex min-w-0 flex-wrap items-center gap-2">
                  <span class="font-mono text-sm font-medium">{{ service.service }}</span>
                  <Badge v-if="!service.changed" variant="neutral">unchanged</Badge>
                  <Badge v-else-if="service.from === null" variant="success">new</Badge>
                  <Badge v-else-if="service.to === null" variant="warning">removed</Badge>
                  <Badge v-else variant="info">changed</Badge>
                </span>
                <span class="flex items-center gap-2">
                  <span class="flex items-center gap-1.5 font-mono text-xs">
                    <span :class="service.changed ? 'text-muted-foreground line-through decoration-muted-foreground/50' : ''">{{
                      service.from ?? "—"
                    }}</span>
                    <template v-if="service.changed"
                      ><ArrowRight :size="12" aria-label="to" /><span class="font-medium">{{ service.to ?? "—" }}</span></template
                    >
                  </span>
                  <Button
                    v-if="service.to !== null && deployment?.targetId && targetExists"
                    variant="ghost"
                    tone="neutral"
                    size="sm"
                    class="min-h-11 md:min-h-8"
                    :aria-label="`Read ${service.service} logs`"
                    @click="logsService = service.service"
                    ><template #leading><FileText :size="14" /></template><span class="hidden sm:inline">Logs</span></Button
                  >
                </span>
              </li>
            </ul>
          </Panel>
        </div>
        <div class="flex min-w-0 flex-col gap-6">
          <Panel title="Sites" description="Probed through the ingress right after the rollout.">
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
                  ><AlertTriangle :size="12" class="mt-0.5 shrink-0" aria-hidden="true" />{{ site.probe.detail }}</span
                >
              </li>
            </ul>
            <p v-else class="text-sm text-muted-foreground">
              {{ isLive ? "Sites are published and probed once the services are healthy." : "No sites were published by this rollout." }}
            </p>
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
                  >Previous success on this target · {{ detail.data.value.previous.release }}</RouterLink
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
