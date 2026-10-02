<script lang="ts">
import type { ContainerVitals } from "@/domain/servers";
import type { ServiceTopology } from "@/domain/topology";

/** Inputs keep the saved declaration separate from optional runtime observations. */
export interface ServiceMapProps {
  readonly topology: ServiceTopology;
  readonly containers?: readonly ContainerVitals[] | null | undefined;
  readonly selectedService?: string | null | undefined;
  readonly observedAt?: string | null | undefined;
}
</script>
<script setup lang="ts">
import { computed, ref, useId, watch } from "vue";
import {
  AlertTriangle,
  Box,
  Database,
  ExternalLink,
  Globe,
  Lock,
  Network,
  ArrowDownToLine,
  Layers,
  Server,
} from "lucide-vue-next";
import { Button } from "@wow-two-beta/ui-vue/presentation/actions";
import { Badge, EmptyState } from "@wow-two-beta/ui-vue/presentation/display";
import { Measures } from "@/domain/common";
import { CanvasArea } from "@wow-two-beta/ui-vue/presentation/layout";
import { TopologyAvailability } from "@/domain/topology";
import {
  buildServiceMapLayout,
  MapEdgeKind,
  MapNodeKind,
  type ServiceMapNode,
} from "./serviceMap/ServiceMapLayout";
import { describeServiceObservation } from "./serviceMap/ServiceObservation";
import { describeServiceVersion } from "./serviceMap/ServiceVersion";

/** Maps saved Compose services to their shared resources and declared startup dependencies. */
defineOptions({ name: "ServiceMap" });
const props = defineProps<ServiceMapProps>();
const emit = defineEmits<{ select: [name: string] }>();
const arrowId = `topology-arrow-${useId()}`;
const MapMinHeight = 240;
const MapMaxHeight = 520;
const localSelection = ref<string | null>(null);
const showVolumes = ref(false);
const showDependencies = ref(true);
const showSites = ref(true);
const showPlatform = ref(true);
const hasSites = computed(() =>
  props.topology.services.some((service) => (service.sites ?? []).length),
);
const hasNeeds = computed(() =>
  props.topology.services.some((service) => (service.needs ?? []).length),
);
const layout = computed(() =>
  buildServiceMapLayout(props.topology, showVolumes.value, {
    sites: showSites.value,
    platform: showPlatform.value,
  }),
);
/** The canvas grows with the map up to a fixed band, and keeps room for its zoom controls. @internal */
const mapHeight = computed(() =>
  Math.max(MapMinHeight, Math.min(layout.value.height, MapMaxHeight)),
);
/** Names the right-hand column after the layers it currently shows. @internal */
const rightColumn = computed(() =>
  [
    showSites.value && hasSites.value ? "Sites" : null,
    showPlatform.value && hasNeeds.value ? "Platform" : null,
    showVolumes.value && props.topology.volumes.length ? "Named volumes" : null,
  ]
    .filter(Boolean)
    .join(" · "),
);
const selected = computed(
  () =>
    props.topology.services.find(
      (service) =>
        service.name === (localSelection.value ?? props.selectedService),
    ) ?? null,
);
const serviceNodes = computed(() =>
  layout.value.nodes.filter((node) => node.kind === MapNodeKind.Service),
);
const resourceNodes = computed(() =>
  layout.value.nodes.filter((node) => node.kind !== MapNodeKind.Service),
);
const visibleEdges = computed(() =>
  layout.value.edges.filter(
    (edge) => showDependencies.value || edge.kind !== MapEdgeKind.Dependency,
  ),
);
const dependencies = computed(() =>
  props.topology.dependencies.filter(
    (edge) => edge.from === selected.value?.name,
  ),
);
const dependents = computed(() =>
  props.topology.dependencies.filter(
    (edge) => edge.to === selected.value?.name,
  ),
);
const selectedObservation = computed(() =>
  selected.value
    ? describeServiceObservation(selected.value.name, props.containers)
    : null,
);
const isolated = computed(() =>
  props.topology.services.filter((service) => !service.networks.length),
);

/** Clears local declaration details when a different target or release becomes authoritative. */
watch(
  () => [props.topology.targetId, props.topology.release],
  () => {
    localSelection.value = null;
  },
);
/** Follows explicit selections made by the containing workspace without moving keyboard focus. */
watch(
  () => props.selectedService,
  (value) => {
    localSelection.value = value ?? null;
  },
);

/** Opens declaration details even when no matching container has been observed. @internal */
function choose(name: string): void {
  localSelection.value = name;
  emit("select", name);
}
/** Positions a native resource element over its SVG connectors. @internal */
function position(node: ServiceMapNode) {
  return {
    left: `${node.x}px`,
    top: `${node.y}px`,
    width: `${node.width}px`,
    height: `${node.height}px`,
  };
}
/** Reads optional runtime data without claiming the Compose declaration proves health. @internal */
function observation(name: string) {
  return describeServiceObservation(name, props.containers);
}
/** Counts only declarations which explicitly reference a hub. @internal */
function members(node: ServiceMapNode): number {
  return props.topology.services.filter((service) =>
    (node.kind === MapNodeKind.Network
      ? service.networks
      : node.kind === MapNodeKind.Platform
        ? (service.needs ?? [])
        : service.volumes
    ).includes(node.name as never),
  ).length;
}
/** The version a service runs: its own when the release records one, else the release. @internal */
function versionOf(name: string) {
  const service = props.topology.services.find((item) => item.name === name);
  return service ? describeServiceVersion(service, props.topology.release) : null;
}
/** Formats the limited Compose startup conditions as operator-facing labels. @internal */
function condition(value: string | null): string {
  return value?.replaceAll("_", " ") ?? "startup order";
}
</script>
<template>
  <div class="min-w-0" data-testid="service-map">
    <EmptyState
      v-if="props.topology.availability !== TopologyAvailability.Available"
      :title="
        props.topology.availability === TopologyAvailability.NotDeployed
          ? 'No deployed service map'
          : 'Service map unavailable'
      "
      :description="
        props.topology.availability === TopologyAvailability.NotDeployed
          ? 'A verified deployment will provide the Compose services for this environment.'
          : 'The saved Compose declaration could not be read. Runtime readings remain separate.'
      "
    />
    <template v-else>
      <div class="mb-4 flex flex-wrap items-center justify-between gap-3">
        <div class="min-w-0 text-xs text-muted-foreground">
          <p class="font-medium text-foreground">
            {{ props.topology.services.length }} declared services
          </p>
          <p class="mt-1 break-all">
            {{ props.topology.release ?? "Verified release" }} · Read
            {{ Measures.moment(props.topology.collectedAt) }}
          </p>
        </div>
        <div class="flex flex-wrap gap-1.5" aria-label="Map layers">
          <Button
            size="sm"
            variant="outline"
            tone="neutral"
            :aria-pressed="showDependencies"
            @click="showDependencies = !showDependencies"
          >
            <template #leading><ArrowDownToLine :size="14" /></template>Startup
            order
          </Button>
          <Button
            v-if="hasSites"
            size="sm"
            variant="outline"
            tone="neutral"
            :aria-pressed="showSites"
            @click="showSites = !showSites"
          >
            <template #leading><Globe :size="14" /></template>Sites
          </Button>
          <Button
            v-if="hasNeeds"
            size="sm"
            variant="outline"
            tone="neutral"
            :aria-pressed="showPlatform"
            @click="showPlatform = !showPlatform"
          >
            <template #leading><Server :size="14" /></template>Platform
          </Button>
          <Button
            v-if="props.topology.volumes.length"
            size="sm"
            variant="outline"
            tone="neutral"
            :aria-pressed="showVolumes"
            @click="showVolumes = !showVolumes"
          >
            <template #leading><Database :size="14" /></template>Volumes
          </Button>
        </div>
      </div>
      <EmptyState
        v-if="!props.topology.services.length"
        title="No services declared"
        description="The saved Compose declaration contains no services."
      />
      <template v-else>
        <div
          class="mb-3 flex flex-wrap gap-x-4 gap-y-2 text-xs text-muted-foreground"
          aria-label="Map legend"
        >
          <span class="inline-flex items-center gap-2"
            ><span class="h-px w-5 bg-primary" />Shared network</span
          >
          <span v-if="showDependencies" class="inline-flex items-center gap-2">
            <span class="w-5 border-t border-dashed border-accent" />A → B: A
            waits for B</span
          >
          <span v-if="showVolumes" class="inline-flex items-center gap-2"
            ><Database :size="12" />Named volume</span
          >
          <span v-if="showSites && hasSites" class="inline-flex items-center gap-2"
            ><span class="h-px w-5 bg-success" />Site the ingress routes</span
          >
          <span v-if="showPlatform && hasNeeds" class="inline-flex items-center gap-2"
            ><span class="h-px w-5 bg-muted-foreground" />Platform service it needs</span
          >
        </div>
        <p class="mb-2 text-xs text-muted-foreground">
          Drag to move · Pinch or Ctrl + scroll to zoom · Select a service for
          details
        </p>
        <CanvasArea
          class="wh-map-surface rounded-xl bg-card"
          :style="{ height: `${mapHeight}px` }"
          aria-label="Compose service relationships"
          :labels="{ canvas: 'service map' }"
        >
          <div
            class="relative"
            :style="{
              width: `${layout.width}px`,
              height: `${layout.height}px`,
            }"
          >
            <p
              class="absolute left-4 top-4 text-[10px] font-semibold uppercase tracking-wider text-muted-foreground"
            >
              Networks
            </p>
            <p
              class="absolute left-[244px] top-4 text-[10px] font-semibold uppercase tracking-wider text-muted-foreground"
            >
              Services
            </p>
            <p
              v-if="rightColumn"
              class="absolute left-[568px] top-4 text-[10px] font-semibold uppercase tracking-wider text-muted-foreground"
            >
              {{ rightColumn }}
            </p>
            <svg
              class="pointer-events-none absolute inset-0"
              :width="layout.width"
              :height="layout.height"
              aria-hidden="true"
            >
              <defs>
                <marker
                  :id="arrowId"
                  markerWidth="7"
                  markerHeight="7"
                  refX="6"
                  refY="3.5"
                  orient="auto"
                >
                  <path d="M 0 0 L 7 3.5 L 0 7 z" fill="var(--color-accent)" />
                </marker>
              </defs>
              <path
                v-for="edge in visibleEdges"
                :key="edge.id"
                :d="edge.path"
                fill="none"
                :stroke="
                  edge.kind === MapEdgeKind.Dependency
                    ? 'var(--color-accent)'
                    : edge.kind === MapEdgeKind.Volume
                      ? 'var(--color-info)'
                      : edge.kind === MapEdgeKind.Site
                        ? 'var(--color-success)'
                        : edge.kind === MapEdgeKind.Platform
                          ? 'var(--color-muted-foreground)'
                          : 'var(--color-primary)'
                "
                :stroke-width="
                  selected && edge.services.includes(selected.name) ? 2.5 : 1.25
                "
                :opacity="
                  !selected || edge.services.includes(selected.name)
                    ? 0.8
                    : 0.18
                "
                :stroke-dasharray="
                  edge.kind === MapEdgeKind.Dependency ? '5 4' : undefined
                "
                :marker-end="
                  edge.kind === MapEdgeKind.Dependency
                    ? `url(#${arrowId})`
                    : undefined
                "
              >
                <title>{{ edge.description }}</title>
              </path>
            </svg>
            <template v-for="node in resourceNodes" :key="node.id">
              <a
                v-if="node.kind === MapNodeKind.Site && node.href"
                :href="node.href"
                target="_blank"
                rel="noopener noreferrer"
                :title="
                  node.attention
                    ? `${node.href} did not answer after the last deploy`
                    : node.href
                "
                :style="position(node)"
                class="absolute flex items-center gap-2 rounded-xl border bg-card px-3 shadow-sm transition-colors hover:bg-muted focus-visible:outline-2 focus-visible:outline-ring"
                :class="node.attention ? 'border-warning' : 'border-border-strong'"
              >
                <AlertTriangle
                  v-if="node.attention"
                  :size="17"
                  class="shrink-0 text-warning"
                  aria-hidden="true"
                />
                <Lock
                  v-else-if="node.subtitle === 'Private site'"
                  :size="17"
                  class="shrink-0 text-success"
                  aria-hidden="true"
                />
                <Globe v-else :size="17" class="shrink-0 text-success" aria-hidden="true" />
                <div class="min-w-0">
                  <p class="flex items-center gap-1 truncate text-xs font-medium">
                    {{ node.name }}<ExternalLink :size="11" aria-hidden="true" />
                  </p>
                  <p class="mt-1 text-[10px] text-muted-foreground">
                    {{ node.attention ? "Did not answer" : node.subtitle }}
                  </p>
                </div>
                <span class="sr-only">, opens in a new tab</span>
              </a>
              <div
                v-else
                :style="position(node)"
                class="absolute flex items-center gap-2 rounded-xl border border-border-strong bg-card px-3 shadow-sm"
              >
                <Network
                  v-if="node.kind === MapNodeKind.Network"
                  :size="17"
                  class="shrink-0 text-primary"
                />
                <Globe
                  v-else-if="node.kind === MapNodeKind.Site"
                  :size="17"
                  class="shrink-0 text-muted-foreground"
                />
                <Server
                  v-else-if="node.kind === MapNodeKind.Platform"
                  :size="17"
                  class="shrink-0 text-muted-foreground"
                />
                <Database v-else :size="17" class="shrink-0 text-info" />
                <div class="min-w-0">
                  <p class="truncate text-xs font-medium" :title="node.name">
                    {{ node.name }}
                  </p>
                  <p class="mt-1 text-[10px] text-muted-foreground">
                    <template v-if="node.kind === MapNodeKind.Site"
                      >{{ node.subtitle }} · no host</template
                    >
                    <template v-else-if="node.kind === MapNodeKind.Platform"
                      >Host service · {{ members(node) }} services</template
                    >
                    <template v-else
                      >{{ node.external ? "External · " : ""
                      }}{{ members(node) }} services</template
                    >
                  </p>
                </div>
              </div>
            </template>
            <button
              v-for="node in serviceNodes"
              :key="node.id"
              type="button"
              :style="position(node)"
              :aria-pressed="selected?.name === node.name"
              :aria-label="`Inspect ${node.name}: ${observation(node.name).label}`"
              class="absolute flex flex-col justify-center rounded-xl border px-4 text-left shadow-sm transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background"
              :class="
                selected?.name === node.name
                  ? 'border-primary bg-primary-soft'
                  : 'border-border-strong bg-card hover:border-primary hover:bg-muted'
              "
              @click="choose(node.name)"
            >
              <span
                class="flex min-w-0 items-center gap-2 text-sm font-semibold"
                ><Box :size="16" class="shrink-0 text-primary" />
                <span class="truncate" :title="node.name">{{
                  node.name
                }}</span
                ><span
                  v-if="versionOf(node.name)"
                  class="ml-auto max-w-24 shrink-0 truncate font-mono text-[10px] font-normal text-muted-foreground"
                  :title="versionOf(node.name)!.detail"
                  >{{ versionOf(node.name)!.label }}</span
                ></span
              >
              <span
                class="mt-2 flex items-center gap-2 text-[11px] text-muted-foreground"
              >
                <span
                  class="size-1.5 shrink-0 rounded-full"
                  :class="{
                    'bg-success': observation(node.name).tone === 'success',
                    'bg-destructive': observation(node.name).tone === 'danger',
                    'bg-warning': observation(node.name).tone === 'warning',
                    'bg-info': observation(node.name).tone === 'info',
                    'bg-muted-foreground':
                      observation(node.name).tone === 'neutral',
                  }"
                />{{ observation(node.name).label }}</span
              >
            </button>
          </div>
        </CanvasArea>
        <p class="mt-2 text-xs text-muted-foreground">
          Lines show Compose membership and startup order; application traffic
          is not measured.
        </p>
        <p
          v-if="!props.topology.dependencies.length"
          class="mt-1 text-xs text-muted-foreground"
        >
          No startup dependencies are declared.
        </p>
        <p v-if="isolated.length" class="mt-1 text-xs text-muted-foreground">
          No Compose network membership:
          {{ isolated.map((service) => service.name).join(", ") }}.
        </p>
        <section
          v-if="selected"
          class="mt-4 rounded-xl border border-primary/30 bg-primary-soft/40 p-4"
          aria-label="Selected service declaration"
          aria-live="polite"
        >
          <div class="flex flex-wrap items-center justify-between gap-2">
            <h3 class="flex items-center gap-2 text-sm font-semibold">
              <Layers :size="16" />{{ selected.name }}
            </h3>
            <Badge :variant="selectedObservation?.tone ?? 'neutral'">{{
              selectedObservation?.label
            }}</Badge>
          </div>
          <p class="mt-2 break-all font-mono text-xs text-muted-foreground">
            {{ selected.image ?? "Image not declared" }}
          </p>
          <p v-if="props.observedAt" class="mt-2 text-xs text-muted-foreground">
            Runtime read {{ Measures.moment(props.observedAt) }}
          </p>
          <dl class="mt-4 grid gap-3 text-xs sm:grid-cols-2">
            <div>
              <dt class="font-medium">Version</dt>
              <dd class="mt-1 break-words font-mono text-muted-foreground">
                {{ versionOf(selected.name)?.label }}
              </dd>
              <dd class="mt-1 text-muted-foreground">
                {{ versionOf(selected.name)?.detail }}
              </dd>
            </div>
            <div v-if="(selected.needs ?? []).length">
              <dt class="font-medium">Platform services</dt>
              <dd class="mt-1 break-words text-muted-foreground">
                {{ selected.needs.join(", ") }}
              </dd>
            </div>
            <div v-if="(selected.sites ?? []).length" class="sm:col-span-2">
              <dt class="font-medium">Sites</dt>
              <dd class="mt-1 text-muted-foreground">
                <ul class="space-y-1">
                  <li v-for="site in selected.sites" :key="site.name + site.path">
                    <a
                      v-if="site.url"
                      :href="site.url"
                      target="_blank"
                      rel="noopener noreferrer"
                      class="font-medium text-primary hover:underline"
                      >{{ site.name }}{{ site.path === "/" ? "" : site.path }}</a
                    ><span v-else class="font-medium">{{ site.name }}{{ site.path === "/" ? "" : site.path }}</span>
                    · {{ site.exposure }} · port {{ site.port
                    }}{{
                      site.reachable === false
                        ? " · did not answer after the last deploy"
                        : site.reachable
                          ? " · answered after the last deploy"
                          : ""
                    }}
                  </li>
                </ul>
              </dd>
            </div>
            <div>
              <dt class="font-medium">Networks</dt>
              <dd class="mt-1 break-words text-muted-foreground">
                {{ selected.networks.join(", ") || "None declared" }}
              </dd>
            </div>
            <div>
              <dt class="font-medium">Published / container ports</dt>
              <dd class="mt-1 break-words font-mono text-muted-foreground">
                {{ selected.ports.join(", ") || "None declared" }}
              </dd>
            </div>
            <div>
              <dt class="font-medium">Named volumes</dt>
              <dd class="mt-1 break-words text-muted-foreground">
                {{ selected.volumes.join(", ") || "None declared" }}
              </dd>
            </div>
            <div>
              <dt class="font-medium">Startup dependencies</dt>
              <dd class="mt-1 text-muted-foreground">
                <span v-if="!dependencies.length">None declared</span>
                <ul v-else class="space-y-1">
                  <li v-for="edge in dependencies" :key="edge.to">
                    {{ edge.to }} · {{ condition(edge.condition)
                    }}{{ edge.required ? "" : " · optional" }}
                  </li>
                </ul>
              </dd>
            </div>
          </dl>
          <p
            v-if="dependents.length"
            class="mt-3 text-xs text-muted-foreground"
          >
            Waited on by {{ dependents.map((edge) => edge.from).join(", ") }}.
          </p>
        </section>
      </template>
    </template>
    <ul
      v-if="props.topology.warnings.length"
      class="mt-4 space-y-1 rounded-xl bg-warning-soft p-3 text-xs text-warning-soft-foreground"
      aria-label="Service map limitations"
    >
      <li v-for="warning in props.topology.warnings" :key="warning">
        {{ warning }}
      </li>
    </ul>
  </div>
</template>
