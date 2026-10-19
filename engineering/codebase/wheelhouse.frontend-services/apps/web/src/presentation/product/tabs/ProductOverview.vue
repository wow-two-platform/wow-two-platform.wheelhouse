<script setup lang="ts">
import { computed, nextTick, ref, watch } from "vue";
import { RouterLink, useRoute, useRouter } from "vue-router";
import {
  ArrowRight,
  Box,
  ChevronRight,
  Layers,
  List,
  Network,
  Rocket,
  Server,
  ShieldCheck,
} from "lucide-vue-next";
import { Button } from "@wow-two-beta/fe/presentation/actions";
import {
  Badge,
  DescriptionGroup,
  EmptyState,
  RelativeTime,
  Text,
} from "@wow-two-beta/fe/presentation/display";
import { ToggleGroup, ToggleInput } from "@wow-two-beta/fe/presentation/forms";
import {
  DataRegion,
  SkeletonState,
  SkeletonStateGroup,
  SkeletonStateSlot,
  useSkeletonItems,
} from "@wow-two-beta/fe/presentation/feedback";
import { PageHeaderActions } from "@wow-two-beta/fe/presentation/layout";
import { ConsolePaths, ProductTab } from "@/application/navigation";
import { useLastEnvironment, useShowInactive } from "@/application/preferences";
import { useProducts } from "@/application/products";
import { useServerVitals } from "@/application/servers";
import {
  useDeploymentTargets,
  useDeploymentHistory,
  useDeploymentPages,
  useTargetState,
  useReleaseArtifacts,
  useDeploymentOutcome,
} from "@/application/deployments";
import { useRefresh } from "@/bootstrap/query";
import { useTargetTopology } from "@/application/topology";
import { buildProductInventory } from "@/application/product/ProductInventory";
import {
  EmptyProductSelection,
  readProductSelection,
  resolveProductSelection,
  writeProductSelection,
  type ProductSelection,
  type SelectionRead,
} from "@/application/product/ProductSelection";
import { Measures } from "@/domain/common";
import {
  DeploymentExtensions,
  EntityKeys,
  RangePreset,
  TargetCondition,
  type DeploymentFilterState,
  type DeploymentJob,
} from "@/domain/deployments";
import { ProductStatusRules } from "@/domain/products";
import { ServerExtensions } from "@/domain/servers";
import { EnvironmentOrder, type DeploymentEnvironment } from "@/domain/targets";
import { withoutServices } from "@/domain/topology";
import {
  DeployModal,
  DeploymentStatusPill,
  DeploymentSteps,
  ReconcileModal,
  ServiceLogsModal,
  useDeployModal,
} from "@/presentation/deployments";
import { InfoPopover, RefreshButton } from "@/presentation/common/components";
import { ResourceMeter } from "@/presentation/servers";
import BuildUsagePanel from "../BuildUsagePanel.vue";
import HostingPanel from "@/presentation/infrastructure/components/HostingPanel.vue";
import EnvironmentCompare from "../EnvironmentCompare.vue";
import ServiceActions from "../ServiceActions.vue";
import ServiceMap from "../ServiceMap.vue";
import ServicesInfo from "../ServicesInfo.vue";

/**
 * A product's Overview tab: its environments, services and deployments, scoped by the environment the top bar's
 * segmented control picks, with the inspector beside them from `xl`. The URL carries the scope:
 * `/p/{product}?env=…&inspect=…&item=…`. The product page above it draws the header and the tabs.
 */
defineOptions({ name: "ProductOverview" });
const emit = defineEmits<{ addEnvironment: [] }>();
const route = useRoute();
const router = useRouter();
const products = useProducts();
const targets = useDeploymentTargets();
const vitals = useServerVitals();
const history = useDeploymentHistory();
const releases = useReleaseArtifacts();
const deploy = useDeployModal();
const lastEnvironment = useLastEnvironment();
const showInactive = useShowInactive();
const reconciling = ref(false);
const inspectorElement = ref<HTMLElement | null>(null);
/** The service whose container output the log viewer reads; null while it is closed. */
const logsService = ref<string | null>(null);
const productKey = computed(() => {
  const value = route.params["product"];
  const item = Array.isArray(value) ? value[0] : value;
  return typeof item === "string" ? item : null;
});
const inventory = computed(() =>
  buildProductInventory(products.products.value, targets.data.value ?? []),
);
/** The page's query as literal parameters, so invalid explicit values stay visible to the resolver. */
const search = computed(
  () => new URLSearchParams(route.fullPath.split("?")[1]?.split("#")[0] ?? ""),
);
/**
 * The requested selection. Without an environment in the address, the one last chosen on any product page applies
 * when this product has it; the address is then rewritten to name it.
 */
const requested = computed<ProductSelection>(() => {
  const read = readProductSelection(productKey.value, search.value);
  if (read.environment !== null || read.targetId !== null) return read;
  const remembered = lastEnvironment.value;
  const entry = inventory.value.products.find((item) => item.key === productKey.value);
  return remembered && entry?.targets.some((target) => target.environment === remembered)
    ? { ...read, environment: remembered }
    : read;
});
const inventoryRead = computed(() => {
  if (products.loading.value || targets.loading.value)
    return { status: "loading" } as const;
  if (
    (products.error.value && !products.products.value.length) ||
    (targets.error.value && !targets.data.value)
  )
    return { status: "error" } as const;
  return { status: "ready", data: inventory.value } as const;
});
const selection = computed(() =>
  resolveProductSelection({
    requested: requested.value,
    inventory: inventoryRead.value,
    vitals: read(
      vitals.data.value,
      vitals.loading.value,
      Boolean(vitals.error.value),
    ),
    history: read(
      history.data.value,
      history.loading.value,
      Boolean(history.error.value),
    ),
  }),
);
const product = computed(() => selection.value.product);
const target = computed(() => selection.value.target);
/** The product's catalog record, whose status and inactive services the page shows and changes. */
const record = computed(() => product.value?.product ?? null);
/** The services the page leaves out: the inactive ones, until "Show inactive" is on. */
const hiddenServices = computed(() => (showInactive.value ? [] : (record.value?.inactiveServices ?? [])));
const state = useTargetState(() => target.value?.id ?? null);
const topology = useTargetTopology(() => target.value?.id ?? null);
const serviceView = ref<"map" | "list">("map");
const selectedMapService = ref<string | null>(null);
const targetVitals = computed(() =>
  vitals.data.value?.targets.find((item) => item.targetId === target.value?.id),
);
/** The environment's host, read from its server itself. */
const hostVitals = computed(() =>
  target.value ? ServerExtensions.hostOf(vitals.data.value, target.value.serverId) : null,
);
const mapContainers = computed(() =>
  targetVitals.value?.ok &&
  targetVitals.value.condition === TargetCondition.Ready &&
  state.data.value?.condition === TargetCondition.Ready &&
  topology.data.value?.release &&
  state.data.value.current?.release === topology.data.value.release &&
  targetVitals.value.release === topology.data.value.release
    ? (targetVitals.value.containers ?? null)
    : null,
);
/** The map's declaration and readings without the hidden services. */
const visibleTopology = computed(() =>
  topology.data.value ? withoutServices(topology.data.value, hiddenServices.value) : null,
);
const visibleMapContainers = computed(
  () => mapContainers.value?.filter((item) => !hiddenServices.value.includes(item.service)) ?? null,
);
const targetHistory = computed(() =>
  (history.data.value ?? []).filter((job) => job.targetId === target.value?.id),
);
const inspected = computed(() =>
  selection.value.status === "ready" ? selection.value.inspector : null,
);
const inspectorTitle = computed(() =>
  inspected.value?.kind === "service"
    ? "Service details"
    : inspected.value?.kind === "deployment"
      ? "Deployment details"
      : "Environment details",
);
const outcome = useDeploymentOutcome(
  () => (inspected.value?.kind === "deployment" ? inspected.value.job.id : null),
  () => (inspected.value?.kind === "deployment" ? inspected.value.job.status : null),
);
const inspectedJob = computed(() =>
  inspected.value?.kind === "deployment"
    ? (outcome.data.value ?? inspected.value.job)
    : null,
);
/** The page's first read: the frame renders with placeholder values until the inventory arrives. */
const firstLoad = computed(() => inventoryRead.value.status === "loading");
/** The product's deployments as the history reads them, every attempt: the state, attention and cause each recent
 * submission wears, the same chip as the Deployments views. */
const productHistory = useDeploymentPages(
  () =>
    ({
      entities: productKey.value ? [EntityKeys.product(productKey.value)] : [],
      range: { kind: "preset", preset: RangePreset.Year },
    }) satisfies DeploymentFilterState,
  { limit: 100, everyAttempt: true, enabled: () => Boolean(productKey.value) },
);
const historyItems = computed(() => new Map(productHistory.items.value.map((item) => [item.id, item] as const)));
/** The environment's recent submissions, or placeholder rows until the history and their states first arrive. */
const recentRows = useSkeletonItems(
  () => (history.data.value && !productHistory.loading.value ? targetHistory.value.slice(0, 6) : undefined),
  3,
);
/** What a submission's chip reads: the history's state where it holds the submission at the same runner status (a
 * fresher follow-up outruns it), else its runner status. */
function stateOf(job: DeploymentJob) {
  const item = historyItems.value.get(job.id);
  return item && item.status === job.status
    ? {
        status: item.status,
        state: item.state,
        substate: item.substate,
        attention: item.attention,
        cause: item.cause,
        reason: item.reason,
        watch: item.watch,
      }
    : { status: job.status, reason: job.reason ?? null };
}
/** The environment's observed services, or placeholder cards until the vitals first arrive. */
const serviceCards = useSkeletonItems(
  () =>
    vitals.data.value
      ? (targetVitals.value?.containers ?? []).filter((item) => !hiddenServices.value.includes(item.service))
      : undefined,
  2,
);
const drift = computed(() =>
  target.value
    ? DeploymentExtensions.releaseDrift(
        state.data.value?.current?.release,
        releases.data.value ?? [],
        target.value.product,
      )
    : null,
);
/** Reads the services again: the map's declaration and the containers' vitals. */
const servicesRefresh = useRefresh(() =>
  Promise.all([vitals.refetch(), ...(target.value ? [topology.refetch()] : [])]),
);
const refresh = useRefresh(() =>
  Promise.all([
    products.reload(),
    targets.refetch(),
    vitals.refetch(),
    history.refetch(),
    productHistory.refetch(),
    releases.refetch(),
    ...(target.value ? [state.refetch()] : []),
    ...(target.value ? [topology.refetch()] : []),
    ...(inspected.value?.kind === "deployment" ? [outcome.refetch()] : []),
  ]),
);

/** Whether the product has a target in the environment, so its segment can be chosen. */
function hasEnvironment(environment: DeploymentEnvironment): boolean {
  return Boolean(product.value?.targets.some((item) => item.environment === environment));
}

/** Switches the services view; pressing the active segment clears the group's value and keeps the view. @internal */
function setServiceView(value: unknown): void {
  if (value === "map" || value === "list") serviceView.value = value;
}

/** Clears declaration selection when the owner changes environments. */
watch(
  () => target.value?.id,
  () => {
    selectedMapService.value = null;
  },
);

/**
 * Names the environment in the address once it resolves — an old link's `target`, or the remembered environment —
 * and remembers it, so the next product page and a pinned service open in the same one.
 */
watch(selection, (result) => {
  if (result.status !== "ready" || !result.target) return;
  const environment = result.target.environment;
  if ((EnvironmentOrder as readonly string[]).includes(environment))
    lastEnvironment.value = environment as DeploymentEnvironment;
  if (search.value.get("env") === environment && !search.value.has("target")) return;
  void router.replace({
    path: route.path,
    query: Object.fromEntries(writeProductSelection(search.value, result.selection)),
  });
});

/** Shows runtime details only when one observed container matches the declared service. @internal */
function selectMapService(name: string): void {
  selectedMapService.value = name;
  if (!product.value || !target.value) return;
  const readings =
    mapContainers.value?.filter((item) => item.service === name) ?? [];
  void select({
    productKey: product.value.key,
    environment: target.value.environment,
    targetId: null,
    inspector: readings?.length === 1 ? "service" : "target",
    itemId: readings?.length === 1 ? name : null,
  });
}

/** Converts completed cache values into the selection resolver's read contract. @internal */
function read<T>(
  data: T | undefined,
  loading: boolean,
  error: boolean,
): SelectionRead<T> {
  if (data !== undefined) return { status: "ready", data };
  return { status: loading ? "loading" : error ? "error" : "loading" };
}
/** Writes scoped selection while preserving unrelated URL parameters. @internal */
async function select(
  value: ProductSelection,
  focusInspector = false,
): Promise<void> {
  const params = writeProductSelection(search.value, value);
  await router.push({ path: route.path, query: Object.fromEntries(params) });
  reconciling.value = false;
  if (focusInspector) {
    await nextTick();
    inspectorElement.value?.focus({ preventScroll: true });
    inspectorElement.value?.scrollIntoView({
      behavior: "auto",
      block: "nearest",
    });
  }
}
/** Selects an environment from the top bar; pressing the active segment keeps it. @internal */
function chooseEnvironment(value: unknown): void {
  if (typeof value !== "string" || !product.value || value === target.value?.environment) return;
  void select({ ...EmptyProductSelection, productKey: product.value.key, environment: value });
}
/** Opens a service or submission in the current environment's inspector. @internal */
function inspect(
  kind: "target" | "service" | "deployment",
  id: string | null = null,
): void {
  if (product.value && target.value)
    void select(
      {
        productKey: product.value.key,
        environment: target.value.environment,
        targetId: null,
        inspector: kind,
        itemId: id,
      },
      true,
    );
}
/** Starts the retained deployment flow with an exact target, optionally preselecting a published bundle. @internal */
function openDeploy(job?: DeploymentJob): void {
  if (target.value)
    deploy.openDeploy({
      target: target.value.id,
      release: job?.bundleId ?? "",
    });
}
</script>
<template>
  <div>
    <PageHeaderActions>
      <!-- The environment scopes everything below; a product without a target in one cannot choose it. -->
      <ToggleGroup
        v-if="product?.targets.length"
        variant="segmented"
        aria-label="Environment"
        :model-value="target?.environment ?? null"
        @update:model-value="chooseEnvironment"
      >
        <ToggleInput
          v-for="environment in EnvironmentOrder"
          :key="environment"
          :value="environment"
          size="sm"
          :is-disabled="!hasEnvironment(environment)"
          v-bind="hasEnvironment(environment) ? {} : { title: `No ${environment} environment yet` }"
          >{{ environment }}</ToggleInput
        >
      </ToggleGroup>
      <RefreshButton
        size="md"
        icon-only
        :refreshing="refresh.refreshing.value"
        @refresh="refresh.refresh"
      />
      <Button :is-disabled="!target" @click="openDeploy()"
        ><template #leading><Rocket :size="16" /></template>Deploy</Button
      >
    </PageHeaderActions>
    <!-- The product page's own region answers a product that is not in Wheelhouse. -->
    <DataRegion
      :is-first-load="firstLoad"
      :error="products.error.value ?? targets.error.value"
      :has-data="inventoryRead.status === 'ready'"
      loading-label="Loading the product"
    >
      <div
        class="grid items-start gap-5 xl:grid-cols-[minmax(0,1fr)_280px] 2xl:grid-cols-[minmax(0,1fr)_300px]"
      >
        <section id="product-environment" class="min-w-0 space-y-5">
          <div
            v-if="selection.status === 'unavailable'"
            role="alert"
            class="rounded-2xl border border-warning/30 bg-warning-soft p-5 text-warning-soft-foreground"
          >
            <h2 class="font-semibold">This selection is unavailable</h2>
            <p class="mt-2 text-sm">
              The linked environment or item is no longer available for this product. Choose an environment to
              continue.
            </p>
            <Button
              class="mt-3"
              size="sm"
              variant="outline"
              @click="select({ ...EmptyProductSelection, productKey })"
              >Reset selection</Button
            >
          </div>
          <!-- A product without an environment has nothing to show here yet; adding one is a step away. -->
          <div v-if="product && !product.targets.length" class="rounded-2xl border border-dashed border-border p-5">
            <p class="text-sm font-medium">No environment yet</p>
            <p class="mt-1 text-sm text-muted-foreground">Add one to place this product on a server and deploy it.</p>
            <Button v-if="record" class="mt-3" size="sm" variant="outline" @click="emit('addEnvironment')"
              >Add environment</Button
            >
          </div>
          <template v-if="target">
            <p
              v-if="state.error.value"
              role="alert"
              class="rounded-xl bg-destructive-soft p-3 text-sm text-destructive-soft-foreground"
            >
              {{ state.error.value.message }} Current release verification is
              unavailable.
            </p>
            <DataRegion
              :is-first-load="state.isFirstLoad.value"
              loading-label="Reading the environment"
              class="grid gap-3 sm:grid-cols-2"
            >
              <SkeletonStateGroup class="contents" :is-loading="refresh.refreshing.value">
                <button
                  type="button"
                  class="rounded-2xl border border-border bg-card p-5 text-left focus-visible:outline-2 focus-visible:outline-ring"
                  @click="inspect('target')"
                >
                  <span
                    class="flex items-center gap-2 text-xs text-muted-foreground"
                    ><Rocket :size="15" />Verified release</span
                  ><span class="mt-3 block truncate text-xl font-semibold"
                    ><SkeletonStateSlot>{{
                      state.data.value?.current?.release ?? "Not verified"
                    }}</SkeletonStateSlot></span
                  ><Text as="span" class="mt-2 block text-xs text-muted-foreground">{{
                    drift?.behind != null && drift.behind > 0
                      ? `${drift.behind} newer published release${drift.behind === 1 ? "" : "s"}`
                      : state.data.value?.current
                        ? "Last verified on this target"
                        : "A successful rollout establishes this state"
                  }}</Text>
                </button>
                <button
                  type="button"
                  class="rounded-2xl border border-border bg-card p-5 text-left focus-visible:outline-2 focus-visible:outline-ring"
                  @click="inspect('target')"
                >
                  <span
                    class="flex items-center gap-2 text-xs text-muted-foreground"
                    ><ShieldCheck :size="15" />Deployment state</span
                  ><span class="mt-3 block text-xl font-semibold capitalize"
                    ><SkeletonStateSlot>{{
                      state.data.value
                        ? DeploymentExtensions.label(state.data.value.condition)
                        : "Unknown"
                    }}</SkeletonStateSlot></span
                  ><span class="mt-2 block text-xs text-muted-foreground"
                    >{{ target.host }} · {{ target.provider }}</span
                  >
                </button>
              </SkeletonStateGroup>
            </DataRegion>
            <EnvironmentCompare
              v-if="product"
              :targets="product.targets"
              :releases="releases.data.value ?? []"
              :hidden-services="hiddenServices"
              @promote="deploy.openDeploy($event)"
            />
            <BuildUsagePanel v-if="productKey" :product="productKey" />
            <HostingPanel v-if="record" :product="record" />
            <section
              class="wh-glass rounded-2xl border border-border p-5"
              aria-labelledby="services-heading"
              data-info-scope
            >
              <div
                class="mb-4 flex flex-wrap items-center justify-between gap-2"
              >
                <div class="flex items-center gap-1">
                  <h3 id="services-heading" class="flex items-center gap-2 text-sm font-semibold">
                    <Box :size="16" aria-hidden="true" />Services
                  </h3>
                  <ServicesInfo />
                </div>
                <span class="flex items-center gap-2">
                <RefreshButton
                  variant="ghost"
                  size="md"
                  icon-only
                  label="Refresh services"
                  class="max-md:size-11"
                  :refreshing="servicesRefresh.refreshing.value"
                  @refresh="servicesRefresh.refresh"
                />
                <ToggleGroup
                  variant="segmented"
                  aria-label="Service view"
                  :model-value="serviceView"
                  @update:model-value="setServiceView"
                >
                  <ToggleInput value="map" size="sm" class="max-md:min-h-9"
                    ><Network :size="14" />Map</ToggleInput
                  >
                  <ToggleInput value="list" size="sm" class="max-md:min-h-9"
                    ><List :size="14" />List</ToggleInput
                  >
                </ToggleGroup>
                </span>
              </div>
              <!-- The map cannot be drawn without its topology, so its first load shows the map's frame. -->
              <DataRegion
                v-if="serviceView === 'map'"
                :is-first-load="topology.isFirstLoad.value"
                :is-refreshing="topology.isRefreshing.value"
                :error="topology.error.value"
                :has-data="Boolean(topology.data.value)"
                loading-label="Loading the service map"
              >
                <template #skeleton
                  ><SkeletonState class="h-72 w-full rounded-xl"
                /></template>
                <SkeletonStateGroup class="contents" :is-loading="refresh.refreshing.value">
                  <ServiceMap
                    v-if="visibleTopology"
                    :key="target.id"
                    :topology="visibleTopology"
                    :containers="visibleMapContainers"
                    :selected-service="
                      selectedMapService ??
                      (inspected?.kind === 'service'
                        ? inspected.service.service
                        : null)
                    "
                    :observed-at="
                      mapContainers
                        ? (vitals.data.value?.collectedAt ?? null)
                        : null
                    "
                    @select="selectMapService"
                  />
                </SkeletonStateGroup>
              </DataRegion>
              <DataRegion
                v-else
                :is-first-load="vitals.isFirstLoad.value"
                :is-refreshing="vitals.isRefreshing.value"
                :error="vitals.error.value"
                :has-data="Boolean(vitals.data.value)"
                loading-label="Reading the environment's services"
              >
                <p
                  v-if="vitals.data.value && (!targetVitals || !targetVitals.ok)"
                  role="status"
                  class="rounded-xl bg-warning-soft p-4 text-sm text-warning-soft-foreground"
                >
                  {{
                    targetVitals?.reason ??
                    "This environment has no readable vitals yet."
                  }}
                </p>
                <SkeletonStateGroup v-else class="contents" :is-loading="refresh.refreshing.value">
                  <p
                    v-for="problem in targetVitals?.problems ?? []"
                    :key="problem"
                    class="mb-3 text-sm text-warning"
                  >
                    {{ problem }}
                  </p>
                  <div class="grid gap-3 sm:grid-cols-2">
                    <!-- The card's name is a stretched button that opens the inspector; the controls sit above it. -->
                    <div
                      v-for="(service, index) in serviceCards"
                      :key="service?.service ?? `placeholder-${index}`"
                      class="relative rounded-xl border p-4 transition-colors focus-within:border-primary/50 hover:border-primary/50"
                      :class="[
                        service &&
                        inspected?.kind === 'service' &&
                        inspected.service.service === service.service
                          ? 'border-primary bg-primary-soft/40'
                          : 'border-border bg-background/40',
                        service && !ProductStatusRules.isServiceActive(record, service.service) ? 'opacity-60' : '',
                      ]"
                    >
                      <span class="flex items-start justify-between gap-2"
                        ><span
                          class="flex size-9 items-center justify-center rounded-lg border border-border bg-card"
                          ><Box :size="18" class="text-primary" /></span
                        ><span class="relative z-10 flex items-center gap-1"
                          ><Badge
                            v-if="service && !ProductStatusRules.isServiceActive(record, service.service)"
                            size="sm"
                            variant="neutral"
                            >Inactive</Badge
                          ><Text
                            as="span"
                            class="text-xs capitalize"
                            :class="
                              service && ServerExtensions.isHealthy(service)
                                ? 'text-success'
                                : 'text-destructive'
                            "
                            >{{ service ? ServerExtensions.containerLabel(service) : "" }}</Text
                          ><ServiceActions v-if="service" :product="record" :service="service.service" /></span
                      ></span>
                      <button
                        type="button"
                        class="mt-4 block max-w-full break-all text-left text-sm font-semibold after:absolute after:inset-0 after:rounded-xl after:content-[''] focus-visible:outline-none"
                        :disabled="!service"
                        @click="service && inspect('service', service.service)"
                      >
                        <Text as="span" class="text-inherit">{{ service?.service }}</Text>
                      </button>
                      <Text as="span" class="mt-1 block text-xs text-muted-foreground"
                        ><template v-if="service">{{ service.restarts }} restarts</template></Text
                      >
                      <span
                        class="mt-4 flex items-center justify-between border-t border-border pt-3 text-xs tabular-nums text-muted-foreground"
                        ><SkeletonStateSlot>{{
                          service?.cpuPercent == null
                            ? "CPU unavailable"
                            : `${service.cpuPercent.toFixed(1)}% CPU`
                        }}</SkeletonStateSlot
                        ><SkeletonStateSlot>{{
                          service ? Measures.bytes(service.memoryBytes) : "0 B"
                        }}</SkeletonStateSlot></span
                      >
                    </div>
                  </div>
                  <template v-if="targetVitals">
                    <p
                      v-if="targetVitals.containers == null"
                      class="py-4 text-sm text-muted-foreground"
                    >
                      Container readings are unavailable.
                    </p>
                    <EmptyState
                      v-else-if="targetVitals.containers.length === 0"
                      title="No containers observed"
                      description="Deploy a published release to start services on this environment."
                    />
                  </template>
                  <div
                    v-if="hostVitals || !vitals.data.value"
                    class="mt-4 grid gap-3 sm:grid-cols-2"
                  >
                    <ResourceMeter
                      label="Host CPU load"
                      :value="hostVitals ? ServerExtensions.loadPercent(hostVitals) : null"
                      :refreshing="refresh.refreshing.value"
                    /><ResourceMeter
                      label="Host memory"
                      :value="hostVitals ? ServerExtensions.memoryPercent(hostVitals) : null"
                      :refreshing="refresh.refreshing.value"
                    />
                  </div>
                </SkeletonStateGroup>
              </DataRegion>
            </section>
            <section
              class="overflow-hidden rounded-2xl border border-border bg-card"
            >
              <header
                class="flex items-center justify-between gap-3 border-b border-border px-5 py-4"
              >
                <h3 class="text-sm font-semibold">Recent deployments</h3>
                <RouterLink
                  :to="ConsolePaths.product(productKey ?? '', ProductTab.Deployments)"
                  class="text-xs font-medium text-primary hover:underline"
                  >View all</RouterLink
                >
              </header>
              <DataRegion
                :is-first-load="history.isFirstLoad.value"
                :is-refreshing="history.isRefreshing.value"
                :error="history.error.value"
                :has-data="Boolean(history.data.value)"
                :is-empty="Boolean(history.data.value) && !targetHistory.length"
                empty-title="No recent deployments"
                empty-description="Recorded submissions for this environment appear here."
                loading-label="Loading recent deployments"
                class="p-3"
              >
                <button
                  v-for="(job, index) in recentRows"
                  :key="job?.id ?? `placeholder-${index}`"
                  type="button"
                  class="flex w-full items-center gap-3 rounded-xl p-3 text-left hover:bg-muted/50 focus-visible:outline-2 focus-visible:outline-ring"
                  :class="
                    job && inspectedJob?.id === job.id ? 'bg-primary-soft' : ''
                  "
                  @click="job && inspect('deployment', job.id)"
                >
                  <span class="min-w-0 flex-1"
                    ><Text as="span" class="block truncate text-sm font-medium text-inherit" skeleton-width="8rem">{{
                      job ? (job.release ?? job.bundleId ?? "Submitted release") : ""
                    }}</Text
                    ><Text as="span" class="text-xs text-muted-foreground" skeleton-width="10rem"
                      ><template v-if="job"
                        >{{ job.actor ?? "Unknown actor" }} ·
                        <RelativeTime v-if="job.submittedAt" :value="job.submittedAt" /><template v-else
                          >—</template
                        ></template
                      ></Text
                    ></span
                  ><DeploymentStatusPill
                    v-if="job"
                    v-bind="stateOf(job)"
                    :has-cause="false"
                    class="shrink-0"
                  /><ChevronRight
                    :size="14"
                    class="shrink-0 text-muted-foreground"
                  />
                </button>
              </DataRegion>
            </section>
          </template>
        </section>
      <aside
        ref="inspectorElement"
        tabindex="-1"
        aria-label="Context inspector"
        data-info-scope
        class="wh-glass min-w-0 rounded-2xl border border-border outline-none xl:sticky xl:top-6"
      >
        <header
          class="flex items-center gap-2 border-b border-border px-5 py-4"
        >
          <Layers :size="16" class="text-muted-foreground" aria-hidden="true" />
          <h2 class="text-sm font-semibold">{{ inspectorTitle }}</h2>
          <InfoPopover v-if="inspected?.kind !== 'deployment'" :title="inspectorTitle">
            <p v-if="inspected?.kind === 'service'">
              One container's readings, from the host every minute. Readings are observational: a deploy always updates
              the whole environment, never one service.
            </p>
            <p v-else>
              The environment's host and the release last verified on it. Check a release reads the target's readiness
              against the release you pick; no rollout starts without your confirmation.
            </p>
          </InfoPopover>
        </header>
        <!-- A selection waiting on its read draws the inspector's copy as a placeholder. -->
        <DataRegion
          :is-first-load="selection.status === 'loading'"
          loading-label="Loading the selected item"
          class="p-5"
        >
          <div class="space-y-5">
            <template v-if="target && selection.status === 'ready'">
              <template v-if="inspected?.kind === 'service'"
                ><div>
                  <Box :size="26" class="mb-4 text-primary" />
                  <div class="flex items-start justify-between gap-2">
                    <h3 class="break-all text-lg font-semibold">
                      {{ inspected.service.service }}
                    </h3>
                    <ServiceActions :product="record" :service="inspected.service.service" />
                  </div>
                  <p class="mt-1 text-sm capitalize text-muted-foreground">
                    {{ ServerExtensions.containerLabel(inspected.service) }}
                  </p>
                </div>
                <ResourceMeter
                  label="Container CPU"
                  :value="inspected.service.cpuPercent"
                />
                <DescriptionGroup
                  layout="stacked"
                  density="lg"
                  class="[&_dt]:text-xs"
                  :items="[
                    {
                      label: 'Memory',
                      value: `${Measures.bytes(inspected.service.memoryBytes)} / ${Measures.bytes(inspected.service.memoryLimitBytes)}`,
                    },
                    { label: 'Restarts', value: inspected.service.restarts },
                    {
                      label: 'Started',
                      value: Measures.moment(inspected.service.startedAt),
                    },
                    {
                      label: 'Exit code',
                      value: inspected.service.exitCode ?? 'Not reported',
                    },
                  ]"
                />
                <Button
                  variant="outline"
                  is-full-width
                  @click="logsService = inspected.service.service"
                  >Read logs</Button
                ></template
              >
              <template v-else-if="inspectedJob"
                ><div>
                  <Rocket :size="26" class="mb-4 text-primary" />
                  <h3 class="break-all text-lg font-semibold">
                    {{ inspectedJob.release ?? "Deployment" }}
                  </h3>
                  <DeploymentStatusPill class="mt-2" v-bind="stateOf(inspectedJob)" />
                </div>
                <p
                  v-if="outcome.error.value"
                  role="alert"
                  class="text-sm text-destructive"
                >
                  Outcome refresh failed. The last recorded submission remains
                  visible.
                </p>
                <DescriptionGroup
                  layout="stacked"
                  density="lg"
                  class="[&_dt]:text-xs"
                  :items="[
                    { label: 'Actor', value: inspectedJob.actor ?? 'Unknown' },
                    {
                      label: 'Submitted',
                      value: Measures.moment(inspectedJob.submittedAt),
                    },
                    {
                      label: 'Completed',
                      value: Measures.moment(inspectedJob.completedAt),
                    },
                  ]"
                />
                <p
                  v-if="inspectedJob.reason || inspectedJob.failure"
                  class="break-words rounded-lg bg-warning-soft p-3 text-sm text-warning-soft-foreground"
                >
                  {{ inspectedJob.failure ?? inspectedJob.reason }}
                </p>
                <p
                  v-for="warning in inspectedJob.warnings ?? []"
                  :key="warning"
                  class="break-words rounded-lg bg-warning-soft p-3 text-sm text-warning-soft-foreground"
                >
                  {{ warning }}
                </p>
                <DeploymentSteps :steps="inspectedJob.steps" />
                <Button
                  variant="outline"
                  is-full-width
                  :is-disabled="
                    !inspectedJob.bundleId ||
                    !releases.data.value?.some(
                      (item) => item.id === inspectedJob?.bundleId,
                    )
                  "
                  @click="openDeploy(inspectedJob)"
                  >Redeploy release</Button
                ></template
              >
              <template v-else
                ><div>
                  <Server :size="26" class="mb-4 text-primary" />
                  <h3 class="text-lg font-semibold">
                    {{ target.environment }}
                  </h3>
                  <p class="mt-1 break-all text-xs text-muted-foreground">
                    {{ target.id }}
                  </p>
                </div>
                <DescriptionGroup
                  layout="stacked"
                  density="lg"
                  class="gap-y-4 [&_dd]:break-all [&_dt]:text-xs"
                  :items="[
                    { label: 'Host', value: target.host },
                    { label: 'Provider', value: target.provider },
                    {
                      label: 'Verified release',
                      value: state.data.value?.current?.release ?? 'Not verified',
                    },
                    {
                      label: 'Host uptime',
                      value: Measures.duration(hostVitals?.uptimeSeconds),
                    },
                  ]"
                />
                <div
                  v-if="
                    state.data.value?.condition ===
                      TargetCondition.NeedsReconciliation &&
                    state.data.value.active
                  "
                  class="rounded-xl bg-warning-soft p-3"
                >
                  <p class="mb-3 text-sm text-warning-soft-foreground">
                    A previous rollout needs review before another deployment.
                  </p>
                  <Button
                    size="sm"
                    variant="outline"
                    @click="reconciling = true"
                    >Reconcile target</Button
                  >
                </div>
                <Button
                  v-else
                  variant="outline"
                  is-full-width
                  @click="openDeploy()"
                  >Check a release<template #trailing
                    ><ArrowRight :size="14" /></template
                ></Button></template
              >
              <div class="border-t border-border pt-4">
                <p class="text-xs text-muted-foreground">Environment</p>
                <button
                  type="button"
                  class="mt-2 flex w-full items-center justify-between text-sm font-medium text-primary"
                  @click="inspect('target')"
                >
                  {{ target.environment }}<ChevronRight :size="14" />
                </button>
              </div>
            </template>
            <template v-else
              ><Text class="text-sm text-muted-foreground" skeleton-width="80%">
                {{
                  selection.status === "loading"
                    ? ""
                    : selection.status === "error"
                      ? "The selected item could not be read. Refresh to try again."
                      : "Select a configured environment or one of its services to inspect it here."
                }}
              </Text>
              </template
            >
          </div>
        </DataRegion>
      </aside>
      </div>
    </DataRegion>
    <DeployModal
      :key="deploy.session.value"
      :open="deploy.open.value"
      :selection="deploy.selection.value"
      @update:open="deploy.onOpenChange"
    />
    <ReconcileModal
      v-if="target && state.data.value?.active"
      :key="target.id"
      :target="target.id"
      :active="state.data.value.active"
      :open="reconciling"
      @update:open="reconciling = $event"
    />
    <ServiceLogsModal
      :open="logsService !== null"
      :target="target?.id ?? null"
      :service="logsService"
      @update:open="!$event && (logsService = null)"
    />
  </div>
</template>
