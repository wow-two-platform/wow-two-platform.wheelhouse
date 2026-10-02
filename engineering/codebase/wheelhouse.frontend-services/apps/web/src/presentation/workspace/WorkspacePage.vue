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
  Plus,
  Rocket,
  Server,
  ShieldCheck,
} from "lucide-vue-next";
import { Button } from "@wow-two-beta/ui-vue/presentation/actions";
import { Badge, EmptyState } from "@wow-two-beta/ui-vue/presentation/display";
import {
  SearchInput,
  SelectPicker,
  SelectPickerTrigger,
  SelectPickerValue,
  SelectPickerContent,
  SelectPickerItem,
} from "@wow-two-beta/ui-vue/presentation/forms";
import { SkeletonState } from "@wow-two-beta/ui-vue/presentation/feedback";
import { useProducts } from "@/application/products";
import { useServerVitals } from "@/application/servers";
import {
  useDeploymentTargets,
  useDeploymentHistory,
  useTargetState,
  useReleaseArtifacts,
  useDeploymentOutcome,
} from "@/application/deployments";
import { useRefresh } from "@/bootstrap/query";
import { useTargetTopology } from "@/application/topology";
import {
  buildWorkspaceInventory,
  type WorkspaceProduct,
} from "@/application/workspace/WorkspaceInventory";
import {
  EmptyWorkspaceSelection,
  readWorkspaceSelection,
  resolveWorkspaceSelection,
  writeWorkspaceSelection,
  type WorkspaceRead,
  type WorkspaceSelection,
} from "@/application/workspace/WorkspaceSelection";
import { Measures } from "@/domain/common";
import {
  DeploymentExtensions,
  TargetCondition,
  type DeploymentJob,
} from "@/domain/deployments";
import { ServerExtensions } from "@/domain/servers";
import {
  DeployModal,
  DeploymentSteps,
  ReconcileModal,
  ServiceLogsModal,
  TargetSites,
  useDeployModal,
} from "@/presentation/deployments";
import { LoadState, PageActions, RefreshButton } from "@/presentation/common/components";
import { ResourceMeter } from "@/presentation/servers";
import EnvironmentCompare from "./EnvironmentCompare.vue";
import ServiceMap from "./ServiceMap.vue";
import { ProductIcon } from "@/presentation/products";

/** Connects products, environments, services, and deployment outcomes in one retained workspace. */
defineOptions({ name: "WorkspacePage" });
const route = useRoute();
const router = useRouter();
const products = useProducts();
const targets = useDeploymentTargets();
const vitals = useServerVitals();
const history = useDeploymentHistory();
const releases = useReleaseArtifacts();
const deploy = useDeployModal();
const search = ref("");
const reconciling = ref(false);
const inspectorElement = ref<HTMLElement | null>(null);
/** The service whose container output the log viewer reads; null while it is closed. */
const logsService = ref<string | null>(null);
const inventory = computed(() =>
  buildWorkspaceInventory(products.products.value, targets.data.value ?? []),
);
const requested = computed(() =>
  readWorkspaceSelection(
    new URLSearchParams(route.fullPath.split("?")[1]?.split("#")[0] ?? ""),
  ),
);
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
  resolveWorkspaceSelection({
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
const state = useTargetState(() => target.value?.id ?? null);
const topology = useTargetTopology(() => target.value?.id ?? null);
const serviceView = ref<"map" | "list">("map");
const selectedMapService = ref<string | null>(null);
const targetVitals = computed(() =>
  vitals.data.value?.targets.find((item) => item.targetId === target.value?.id),
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
const targetHistory = computed(() =>
  (history.data.value ?? []).filter((job) => job.targetId === target.value?.id),
);
const inspected = computed(() =>
  selection.value.status === "ready" ? selection.value.inspector : null,
);
const outcome = useDeploymentOutcome(() =>
  inspected.value?.kind === "deployment" ? inspected.value.job.id : null,
);
const inspectedJob = computed(() =>
  inspected.value?.kind === "deployment"
    ? (outcome.data.value ?? inspected.value.job)
    : null,
);
const visibleProducts = computed(() =>
  inventory.value.products.filter((entry) =>
    productName(entry).toLowerCase().includes(search.value.toLowerCase()),
  ),
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
const refresh = useRefresh(() =>
  Promise.all([
    products.reload(),
    targets.refetch(),
    vitals.refetch(),
    history.refetch(),
    releases.refetch(),
    ...(target.value ? [state.refetch()] : []),
    ...(target.value ? [topology.refetch()] : []),
    ...(inspected.value?.kind === "deployment" ? [outcome.refetch()] : []),
  ]),
);

/** Clears declaration selection when the operator changes environments. */
watch(
  () => target.value?.id,
  () => {
    selectedMapService.value = null;
  },
);

/** Shows runtime details only when one observed container matches the declared service. @internal */
function selectMapService(name: string): void {
  selectedMapService.value = name;
  if (!product.value || !target.value) return;
  const readings =
    mapContainers.value?.filter((item) => item.service === name) ?? [];
  void select({
    productKey: product.value.key,
    targetId: target.value.id,
    inspector: readings?.length === 1 ? "service" : "target",
    itemId: readings?.length === 1 ? name : null,
  });
}

/** Converts completed cache values into the selection resolver's read contract. @internal */
function read<T>(
  data: T | undefined,
  loading: boolean,
  error: boolean,
): WorkspaceRead<T> {
  if (data !== undefined) return { status: "ready", data };
  return { status: loading ? "loading" : error ? "error" : "loading" };
}
/** Names a catalog product by its name, and a product outside the catalog by its slug. @internal */
function productName(entry: WorkspaceProduct): string {
  return entry.product?.name ?? entry.key;
}
/** Collects recent recorded outcomes for the product row. @internal */
function recent(entry: WorkspaceProduct): DeploymentJob[] {
  const ids = new Set(entry.targets.map((item) => item.id));
  return (history.data.value ?? [])
    .filter((job) => job.targetId && ids.has(job.targetId))
    .slice(0, 8)
    .reverse();
}
/** Maps semantic outcome roles to the miniature navigation strip. @internal */
function outcomeColor(job: DeploymentJob): string {
  const variant = DeploymentExtensions.statusVariant(job.status);
  return variant === "success"
    ? "bg-success"
    : variant === "danger"
      ? "bg-destructive"
      : variant === "warning"
        ? "bg-warning"
        : variant === "info"
          ? "bg-info"
          : "bg-muted-foreground";
}
/** Writes scoped selection while preserving unrelated URL parameters. @internal */
async function select(
  value: WorkspaceSelection,
  focusInspector = false,
): Promise<void> {
  const params = writeWorkspaceSelection(
    new URLSearchParams(route.fullPath.split("?")[1]?.split("#")[0] ?? ""),
    value,
  );
  await router.push({ path: "/", query: Object.fromEntries(params) });
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
/** Selects a product and clears any incompatible environment and inspector. @internal */
function selectProduct(entry: WorkspaceProduct): void {
  void select({ ...EmptyWorkspaceSelection, productKey: entry.key });
}
/** Selects the literal target ID, even when environment names repeat. @internal */
function selectTarget(id: string | null): void {
  if (id && product.value)
    void select({
      productKey: product.value.key,
      targetId: id,
      inspector: null,
      itemId: null,
    });
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
        targetId: target.value.id,
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
    <PageActions>
      <RefreshButton
        size="md"
        :refreshing="refresh.refreshing.value"
        @refresh="refresh.refresh"
      />
      <Button :is-disabled="!target" @click="openDeploy()"
        ><template #leading><Rocket :size="16" /></template>Deploy
        release</Button
      >
    </PageActions>
    <LoadState
      :loading="inventoryRead.status === 'loading'"
      :error="products.error.value ?? targets.error.value"
      :has-data="inventoryRead.status === 'ready'"
      :empty="!inventory.products.length"
      empty-title="Your workspace starts with a product"
      empty-description="Register a product, or configure an environment in the server catalog."
      @retry="refresh.refresh"
    >
      <template #skeleton
        ><div class="grid gap-5 lg:grid-cols-[216px_1fr_280px]">
          <SkeletonState class="h-96" /><SkeletonState
            class="h-96"
          /><SkeletonState class="h-96" /></div
      ></template>
      <template #emptyActions
        ><RouterLink
          to="/products"
          class="mt-4 inline-flex rounded-lg bg-primary px-4 py-2 text-sm font-medium text-primary-foreground"
          >Register product</RouterLink
        ></template
      >
      <div
        class="grid items-start gap-5 lg:grid-cols-[200px_minmax(0,1fr)] 2xl:grid-cols-[224px_minmax(0,1fr)_300px] xl:grid-cols-[190px_minmax(0,1fr)_280px]"
      >
        <aside
          aria-label="Product navigator"
          class="wh-glass min-w-0 rounded-2xl border border-border p-3 lg:sticky lg:top-6"
        >
          <div class="mb-4 flex items-center justify-between px-1">
            <h2
              class="text-xs font-semibold uppercase tracking-wider text-muted-foreground"
            >
              Products
              <span class="ml-1 tabular-nums">{{
                inventory.products.length
              }}</span>
            </h2>
            <RouterLink
              to="/products"
              aria-label="Manage products"
              class="rounded-md p-1 text-muted-foreground hover:bg-muted"
              ><Plus :size="16"
            /></RouterLink>
          </div>
          <SearchInput
            v-model="search"
            size="sm"
            placeholder="Find a product"
            aria-label="Find a product"
            class="mb-3"
          />
          <div
            class="flex gap-2 overflow-x-auto lg:block lg:space-y-2 lg:overflow-visible"
          >
            <button
              v-for="entry in visibleProducts"
              :key="entry.key"
              type="button"
              :aria-pressed="product?.key === entry.key"
              class="w-48 shrink-0 rounded-xl border p-3 text-left transition-colors focus-visible:outline-2 focus-visible:outline-ring lg:w-full"
              :class="
                product?.key === entry.key
                  ? 'border-primary/30 bg-primary-soft text-primary-soft-foreground'
                  : 'border-transparent hover:border-border hover:bg-muted/50'
              "
              @click="selectProduct(entry)"
            >
              <span class="mb-3 flex items-center gap-2"
                ><ProductIcon
                  :product-slug="entry.product ? entry.key : null"
                  :name="productName(entry)"
                  size="sm"
                /><span class="min-w-0 truncate text-sm font-semibold">{{
                  productName(entry)
                }}</span></span
              >
              <span class="block text-xs text-muted-foreground"
                >{{ entry.targets.length }} environment{{
                  entry.targets.length === 1 ? "" : "s"
                }}</span
              >
              <span class="mt-3 flex items-center justify-between gap-2"
                ><span class="text-[11px] text-muted-foreground"
                  >Recent deploys</span
                ><span v-if="history.loading.value" class="text-xs">…</span
                ><span v-else-if="history.error.value" class="text-xs"
                  >Unavailable</span
                ><span
                  v-else-if="!recent(entry).length"
                  class="text-xs text-muted-foreground"
                  >None</span
                ><span
                  v-else
                  class="flex gap-0.5"
                  :aria-label="
                    recent(entry)
                      .map((job) => DeploymentExtensions.label(job.status))
                      .join(', ')
                  "
                  ><span
                    v-for="job in recent(entry)"
                    :key="job.id"
                    :title="`${job.release ?? 'Release'}: ${DeploymentExtensions.label(job.status)}`"
                    class="h-4 w-1.5 rounded-sm"
                    :class="outcomeColor(job)" /></span
              ></span>
            </button>
          </div>
          <p
            v-if="!visibleProducts.length"
            class="p-3 text-sm text-muted-foreground"
          >
            No matching products.
          </p>
          <RouterLink
            to="/deployments"
            class="mt-5 flex items-center justify-between border-t border-border px-1 pt-4 text-xs font-medium text-muted-foreground hover:text-primary"
            >Portfolio activity<ArrowRight :size="14"
          /></RouterLink>
        </aside>
        <section id="environment-workspace" class="min-w-0 space-y-5">
          <div
            v-if="selection.status === 'unavailable'"
            role="alert"
            class="rounded-2xl border border-warning/30 bg-warning-soft p-5 text-warning-soft-foreground"
          >
            <h2 class="font-semibold">This selection is unavailable</h2>
            <p class="mt-2 text-sm">
              The linked product, environment, or item is no longer available in
              this scope. Choose a product or environment to continue.
            </p>
            <Button
              class="mt-3"
              size="sm"
              variant="outline"
              @click="select(EmptyWorkspaceSelection)"
              >Reset selection</Button
            >
          </div>
          <template v-if="product">
            <div class="wh-glass rounded-2xl border border-border p-5 sm:p-6">
              <div class="flex flex-wrap items-start justify-between gap-4">
                <div class="min-w-0">
                  <p
                    class="mb-1 text-xs font-medium uppercase tracking-wider text-muted-foreground"
                  >
                    Product workspace
                  </p>
                  <h2
                    class="flex items-center gap-3 text-2xl font-semibold tracking-tight"
                  >
                    <ProductIcon
                      :product-slug="product.product ? product.key : null"
                      :name="productName(product)"
                      size="lg"
                    />{{ productName(product) }}
                  </h2>
                  <p class="mt-2 break-all text-xs text-muted-foreground">
                    {{
                      product.product?.repository.name ??
                      "Not in the product catalog"
                    }}
                  </p>
                </div>
                <Badge
                  v-if="product.product"
                  variant="neutral"
                  class="capitalize"
                  :title="'Portfolio lifecycle, separate from runtime health'"
                  >{{ product.product.lifecycle }}</Badge
                >
              </div>
              <div
                v-if="product.targets.length"
                class="mt-5 flex flex-wrap items-center gap-3 border-t border-border pt-4"
              >
                <Layers :size="16" class="text-muted-foreground" />
                <div class="min-w-0 flex-1">
                  <SelectPicker
                    :model-value="target?.id ?? null"
                    :get-option-label="
                      (id) =>
                        product?.targets.find((item) => item.id === id)
                          ?.environment ?? id
                    "
                    @update:model-value="selectTarget"
                    ><SelectPickerTrigger aria-label="Environment"
                      ><SelectPickerValue
                        placeholder="Choose environment" /></SelectPickerTrigger
                    ><SelectPickerContent
                      ><SelectPickerItem
                        v-for="item in product.targets"
                        :key="item.id"
                        :item-key="item.id"
                        :label="item.environment"
                        ><span>{{ item.environment }}</span
                        ><span class="ml-2 text-xs text-muted-foreground">{{
                          item.host
                        }}</span></SelectPickerItem
                      ></SelectPickerContent
                    ></SelectPicker
                  >
                </div>
                <Button
                  size="sm"
                  variant="ghost"
                  tone="neutral"
                  :is-disabled="!target"
                  @click="inspect('target')"
                  >Details<template #trailing
                    ><ChevronRight :size="14" /></template
                ></Button>
              </div>
              <div v-else class="mt-5 rounded-xl bg-muted/50 p-4">
                <p class="text-sm font-medium">No environment yet</p>
                <p class="mt-1 text-sm text-muted-foreground">
                  No fleet target runs this product; targets are defined in the runner's <code>fleet.py</code>.
                </p>
                <RouterLink
                  to="/products"
                  class="mt-3 inline-flex text-sm font-medium text-primary"
                  >Open in Products<ArrowRight :size="14" class="ml-1"
                /></RouterLink>
              </div>
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
              <div class="grid gap-3 sm:grid-cols-2">
                <button
                  type="button"
                  class="rounded-2xl border border-border bg-card p-5 text-left focus-visible:outline-2 focus-visible:outline-ring"
                  @click="inspect('target')"
                >
                  <span
                    class="flex items-center gap-2 text-xs text-muted-foreground"
                    ><Rocket :size="15" />Verified release</span
                  ><SkeletonState
                    v-if="state.loading.value || refresh.refreshing.value"
                    class="mt-3 h-7 w-2/3"
                  /><span
                    v-else
                    class="mt-3 block truncate text-xl font-semibold"
                    >{{
                      state.data.value?.current?.release ?? "Not verified"
                    }}</span
                  ><span class="mt-2 block text-xs text-muted-foreground">{{
                    drift?.behind != null && drift.behind > 0
                      ? `${drift.behind} newer published release${drift.behind === 1 ? "" : "s"}`
                      : state.data.value?.current
                        ? "Last verified on this target"
                        : "A successful rollout establishes this state"
                  }}</span>
                </button>
                <button
                  type="button"
                  class="rounded-2xl border border-border bg-card p-5 text-left focus-visible:outline-2 focus-visible:outline-ring"
                  @click="inspect('target')"
                >
                  <span
                    class="flex items-center gap-2 text-xs text-muted-foreground"
                    ><ShieldCheck :size="15" />Deployment state</span
                  ><SkeletonState
                    v-if="state.loading.value || refresh.refreshing.value"
                    class="mt-3 h-7 w-2/3"
                  /><span
                    v-else
                    class="mt-3 block text-xl font-semibold capitalize"
                    >{{
                      state.data.value
                        ? DeploymentExtensions.label(state.data.value.condition)
                        : "Unknown"
                    }}</span
                  ><span class="mt-2 block text-xs text-muted-foreground"
                    >{{ target.host }} · {{ target.provider }}</span
                  >
                </button>
              </div>
              <TargetSites
                :sites="state.data.value?.current?.sites"
                :versions="state.data.value?.current?.versions"
              />
              <EnvironmentCompare
                v-if="product"
                :targets="product.targets"
                :releases="releases.data.value ?? []"
                @promote="deploy.openDeploy($event)"
              />
              <section
                class="wh-glass rounded-2xl border border-border p-5"
                aria-label="Environment services"
              >
                <div
                  class="mb-4 flex flex-wrap items-center justify-between gap-2"
                >
                  <h3 class="flex items-center gap-2 text-sm font-semibold">
                    <Box :size="16" />Services
                  </h3>
                  <div
                    role="group"
                    aria-label="Service view"
                    class="flex gap-1 rounded-lg border border-border bg-card p-1"
                  >
                    <Button
                      size="sm"
                      :variant="serviceView === 'map' ? 'solid' : 'ghost'"
                      :aria-pressed="serviceView === 'map'"
                      @click="serviceView = 'map'"
                    >
                      <template #leading><Network :size="14" /></template>Map
                    </Button>
                    <Button
                      size="sm"
                      :variant="serviceView === 'list' ? 'solid' : 'ghost'"
                      :aria-pressed="serviceView === 'list'"
                      @click="serviceView = 'list'"
                    >
                      <template #leading><List :size="14" /></template>List
                    </Button>
                  </div>
                </div>
                <LoadState
                  v-if="serviceView === 'map'"
                  :loading="topology.loading.value && !topology.data.value"
                  :refreshing="refresh.refreshing.value"
                  :error="topology.error.value"
                  :has-data="Boolean(topology.data.value)"
                  @retry="topology.refetch"
                >
                  <template #skeleton
                    ><SkeletonState class="h-72 w-full"
                  /></template>
                  <ServiceMap
                    v-if="topology.data.value"
                    :key="target.id"
                    :topology="topology.data.value"
                    :containers="mapContainers"
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
                </LoadState>
                <LoadState
                  v-else
                  :loading="vitals.loading.value"
                  :error="vitals.error.value"
                  :has-data="Boolean(vitals.data.value)"
                  @retry="vitals.refetch"
                >
                  <p
                    v-if="!targetVitals || !targetVitals.ok"
                    role="status"
                    class="rounded-xl bg-warning-soft p-4 text-sm text-warning-soft-foreground"
                  >
                    {{
                      targetVitals?.reason ??
                      "This environment has no readable vitals yet."
                    }}
                  </p>
                  <template v-else>
                    <p
                      v-for="problem in targetVitals.problems"
                      :key="problem"
                      class="mb-3 text-sm text-warning"
                    >
                      {{ problem }}
                    </p>
                    <div class="grid gap-3 sm:grid-cols-2">
                      <button
                        v-for="service in targetVitals.containers ?? []"
                        :key="service.service"
                        type="button"
                        class="rounded-xl border p-4 text-left transition-colors hover:border-primary/50 focus-visible:outline-2 focus-visible:outline-ring"
                        :class="
                          inspected?.kind === 'service' &&
                          inspected.service.service === service.service
                            ? 'border-primary bg-primary-soft/40'
                            : 'border-border bg-background/40'
                        "
                        @click="inspect('service', service.service)"
                      >
                        <span class="flex items-start justify-between gap-2"
                          ><span
                            class="flex size-9 items-center justify-center rounded-lg border border-border bg-card"
                            ><Box :size="18" class="text-primary" /></span
                          ><span
                            class="text-xs capitalize"
                            :class="
                              ServerExtensions.isHealthy(service)
                                ? 'text-success'
                                : 'text-destructive'
                            "
                            >{{ ServerExtensions.containerLabel(service) }}</span
                          ></span
                        >
                        <span
                          class="mt-4 block break-all text-sm font-semibold"
                          >{{ service.service }}</span
                        ><span class="mt-1 block text-xs text-muted-foreground"
                          >{{ service.restarts }} restarts</span
                        >
                        <span
                          v-if="!refresh.refreshing.value"
                          class="mt-4 flex items-center justify-between border-t border-border pt-3 text-xs tabular-nums text-muted-foreground"
                          ><span>{{
                            service.cpuPercent == null
                              ? "CPU unavailable"
                              : `${service.cpuPercent.toFixed(1)}% CPU`
                          }}</span
                          ><span>{{
                            Measures.bytes(service.memoryBytes)
                          }}</span></span
                        ><SkeletonState v-else class="mt-4 h-7 w-full" />
                      </button>
                    </div>
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
                    <div
                      v-if="targetVitals.host"
                      class="mt-4 grid gap-3 sm:grid-cols-2"
                    >
                      <ResourceMeter
                        label="Host CPU load"
                        :value="ServerExtensions.loadPercent(targetVitals.host)"
                        :refreshing="refresh.refreshing.value"
                      /><ResourceMeter
                        label="Host memory"
                        :value="
                          ServerExtensions.memoryPercent(targetVitals.host)
                        "
                        :refreshing="refresh.refreshing.value"
                      />
                    </div>
                  </template>
                </LoadState>
              </section>
              <section
                class="overflow-hidden rounded-2xl border border-border bg-card"
              >
                <header
                  class="flex items-center justify-between gap-3 border-b border-border px-5 py-4"
                >
                  <h3 class="text-sm font-semibold">Recent deployments</h3>
                  <RouterLink
                    :to="{ path: '/deployments', query: route.query }"
                    class="text-xs font-medium text-primary hover:underline"
                    >View all</RouterLink
                  >
                </header>
                <LoadState
                  :loading="history.loading.value"
                  :error="history.error.value"
                  :has-data="Boolean(history.data.value)"
                  :empty="!targetHistory.length"
                  empty-title="No recent deployments"
                  empty-description="Recorded submissions for this environment appear here."
                  class="p-3"
                  @retry="history.refetch"
                >
                  <button
                    v-for="job in targetHistory.slice(0, 6)"
                    :key="job.id"
                    type="button"
                    class="flex w-full items-center gap-3 rounded-xl p-3 text-left hover:bg-muted/50 focus-visible:outline-2 focus-visible:outline-ring"
                    :class="
                      inspectedJob?.id === job.id ? 'bg-primary-soft' : ''
                    "
                    @click="inspect('deployment', job.id)"
                  >
                    <span
                      class="size-2 shrink-0 rounded-full"
                      :class="outcomeColor(job)"
                    /><span class="min-w-0 flex-1"
                      ><span class="block truncate text-sm font-medium">{{
                        job.release ?? job.bundleId ?? "Submitted release"
                      }}</span
                      ><span class="text-xs text-muted-foreground"
                        >{{ job.actor ?? "Operator" }} ·
                        {{ Measures.moment(job.submittedAt) }}</span
                      ></span
                    ><span class="text-xs capitalize text-muted-foreground">{{
                      DeploymentExtensions.label(job.status)
                    }}</span
                    ><ChevronRight
                      :size="14"
                      class="shrink-0 text-muted-foreground"
                    />
                  </button>
                </LoadState>
              </section>
            </template>
          </template>
        </section>
        <aside
          ref="inspectorElement"
          tabindex="-1"
          aria-label="Context inspector"
          class="wh-glass min-w-0 rounded-2xl border border-border outline-none lg:col-start-2 xl:sticky xl:top-6 xl:col-start-auto"
        >
          <header
            class="flex items-center gap-2 border-b border-border px-5 py-4"
          >
            <Layers :size="16" class="text-muted-foreground" />
            <h2 class="text-sm font-semibold">
              {{
                inspected?.kind === "service"
                  ? "Service details"
                  : inspected?.kind === "deployment"
                    ? "Deployment details"
                    : "Environment details"
              }}
            </h2>
          </header>
          <div class="space-y-5 p-5">
            <template v-if="target && selection.status === 'ready'">
              <template v-if="inspected?.kind === 'service'"
                ><div>
                  <Box :size="26" class="mb-4 text-primary" />
                  <h3 class="break-all text-lg font-semibold">
                    {{ inspected.service.service }}
                  </h3>
                  <p class="mt-1 text-sm capitalize text-muted-foreground">
                    {{ ServerExtensions.containerLabel(inspected.service) }}
                  </p>
                </div>
                <ResourceMeter
                  label="Container CPU"
                  :value="inspected.service.cpuPercent"
                />
                <dl class="space-y-3 text-sm">
                  <div>
                    <dt class="text-xs text-muted-foreground">Memory</dt>
                    <dd>
                      {{ Measures.bytes(inspected.service.memoryBytes) }} /
                      {{ Measures.bytes(inspected.service.memoryLimitBytes) }}
                    </dd>
                  </div>
                  <div>
                    <dt class="text-xs text-muted-foreground">Restarts</dt>
                    <dd>{{ inspected.service.restarts }}</dd>
                  </div>
                  <div>
                    <dt class="text-xs text-muted-foreground">Started</dt>
                    <dd>{{ Measures.moment(inspected.service.startedAt) }}</dd>
                  </div>
                  <div>
                    <dt class="text-xs text-muted-foreground">Exit code</dt>
                    <dd>{{ inspected.service.exitCode ?? "Not reported" }}</dd>
                  </div>
                </dl>
                <Button
                  variant="outline"
                  is-full-width
                  @click="logsService = inspected.service.service"
                  >Read logs</Button
                >
                <p
                  class="border-t border-border pt-4 text-xs text-muted-foreground"
                >
                  Deployments update the entire environment. Service readings
                  are observational.
                </p></template
              >
              <template v-else-if="inspectedJob"
                ><div>
                  <Rocket :size="26" class="mb-4 text-primary" />
                  <h3 class="break-all text-lg font-semibold">
                    {{ inspectedJob.release ?? "Deployment" }}
                  </h3>
                  <Badge
                    class="mt-2"
                    :variant="
                      DeploymentExtensions.statusVariant(inspectedJob.status)
                    "
                    >{{
                      DeploymentExtensions.label(inspectedJob.status)
                    }}</Badge
                  >
                </div>
                <p
                  v-if="outcome.error.value"
                  role="alert"
                  class="text-sm text-destructive"
                >
                  Outcome refresh failed. The last recorded submission remains
                  visible.
                </p>
                <dl class="space-y-3 text-sm">
                  <div>
                    <dt class="text-xs text-muted-foreground">Operator</dt>
                    <dd>{{ inspectedJob.actor ?? "Unknown" }}</dd>
                  </div>
                  <div>
                    <dt class="text-xs text-muted-foreground">Submitted</dt>
                    <dd>{{ Measures.moment(inspectedJob.submittedAt) }}</dd>
                  </div>
                  <div>
                    <dt class="text-xs text-muted-foreground">Completed</dt>
                    <dd>{{ Measures.moment(inspectedJob.completedAt) }}</dd>
                  </div>
                </dl>
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
                <dl class="space-y-4 text-sm">
                  <div>
                    <dt class="text-xs text-muted-foreground">Host</dt>
                    <dd class="mt-1 break-all">{{ target.host }}</dd>
                  </div>
                  <div>
                    <dt class="text-xs text-muted-foreground">Provider</dt>
                    <dd class="mt-1">{{ target.provider }}</dd>
                  </div>
                  <div>
                    <dt class="text-xs text-muted-foreground">
                      Verified release
                    </dt>
                    <dd class="mt-1 break-all">
                      {{ state.data.value?.current?.release ?? "Not verified" }}
                    </dd>
                  </div>
                  <div>
                    <dt class="text-xs text-muted-foreground">Host uptime</dt>
                    <dd class="mt-1">
                      {{ Measures.duration(targetVitals?.host?.uptimeSeconds) }}
                    </dd>
                  </div>
                </dl>
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
                ></Button>
                <p class="text-xs text-muted-foreground">
                  Readiness is checked against the release you select. No
                  rollout starts without confirmation.
                </p></template
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
              ><p class="text-sm text-muted-foreground">
                {{
                  selection.status === "loading"
                    ? "Loading the selected item…"
                    : selection.status === "error"
                      ? "The selected item could not be read. Refresh to try again."
                      : "Select a configured environment or one of its services to inspect it here."
                }}
              </p>
              <RouterLink
                v-if="product?.product"
                to="/products"
                class="inline-flex text-sm text-primary"
                >Open in Products</RouterLink
              ></template
            >
          </div>
        </aside>
      </div>
    </LoadState>
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
