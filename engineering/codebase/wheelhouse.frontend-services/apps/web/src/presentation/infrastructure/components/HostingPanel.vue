<script lang="ts">
import type { Product } from "@/domain/products";

/** Defines a product's Hosting panel. */
export interface HostingPanelProps {
  readonly product: Product;
}
</script>

<script setup lang="ts">
import { computed, ref } from "vue";
import { ChevronRight, Database, ExternalLink, MapPin, ServerCog } from "lucide-vue-next";
import { Button } from "@wow-two-beta/fe/presentation/actions";
import { Badge, RelativeTime } from "@wow-two-beta/fe/presentation/display";
import { Alert, DataRegion } from "@wow-two-beta/fe/presentation/feedback";
import { usePlacementChanges, usePlacements } from "@/application/infrastructure";
import { useProductDescriptor } from "@/application/products";
import { useServers } from "@/application/servers";
import { InfraJobRules, PlacementRules, type Placement } from "@/domain/infrastructure";
import { InfoPopover, StatusPill } from "@/presentation/common/components";
import JobSteps from "./JobSteps.vue";
import PlacementModal from "./PlacementModal.vue";

/**
 * Where the product is placed: each placement, newest first, with its state, its live steps (read every two seconds
 * while one runs) and what it created — the dev and prod databases and each site's node, environment, address, tailnet
 * address and whether it is online. "Place on a server" opens the placement dialog, offered only when the product's
 * deploy.yml calls for something to place: databases for a service that needs PostgreSQL, nodes for private sites. A
 * product that needs neither says so in one line, as does a deploy.yml that cannot be read.
 */
defineOptions({ name: "HostingPanel" });
const props = defineProps<HostingPanelProps>();
defineSlots<{}>();

const placements = usePlacements(() => props.product.slug);
const descriptor = useProductDescriptor(() => props.product.slug);
const operations = usePlacementChanges();
const servers = useServers();
const placing = ref(false);
const list = computed(() => placements.placements.value);
/** What deploy.yml calls for; null until it is read, or when it cannot be. */
const plan = computed(() => {
  const read = descriptor.data.value;
  return read && !read.problem ? PlacementRules.planOf(read) : null;
});
const placeable = computed(() => (plan.value ? PlacementRules.hasWork(plan.value) : false));
/** The one line Hosting shows instead of the button: nothing to place, or why deploy.yml could not be read. */
const note = computed(() => {
  const problem = descriptor.data.value?.problem ?? (descriptor.error.value ? descriptor.error.value.message : null);
  if (problem) return `Placement follows deploy.yml: ${problem}`;
  return plan.value && !placeable.value ? "Nothing to place: no service needs a database and every site is public." : "";
});

/** A server's name, or its slug for one the inventory no longer holds. @internal */
const serverName = (slug: string) => servers.data.value?.find((server) => server.slug === slug)?.name ?? slug;
/** Whether a placement's details start open: the newest, and any still running. @internal */
const startsOpen = (placement: Placement, index: number) => index === 0 || InfraJobRules.isActive(placement.status);
</script>

<template>
  <section class="wh-glass rounded-2xl border border-border p-5" aria-labelledby="hosting-heading" data-info-scope>
    <div class="mb-3 flex flex-wrap items-center gap-2">
      <div class="flex items-center gap-1">
        <h3 id="hosting-heading" class="flex items-center gap-2 text-sm font-semibold">
          <ServerCog :size="16" aria-hidden="true" />Hosting
        </h3>
        <InfoPopover title="Hosting">
          <p>
            A placement creates the product's dev and prod databases on a server when a service needs PostgreSQL, and gives
            each private site its own tailnet node: prod's under the node name, dev's with -dev added. Public sites need
            none: they answer on the server's baseline hosts.
          </p>
        </InfoPopover>
      </div>
      <Button
        v-if="placeable"
        size="sm"
        variant="outline"
        tone="neutral"
        class="ms-auto max-md:min-h-11"
        @click="placing = true"
      >
        <template #leading><MapPin :size="14" /></template>Place on a server
      </Button>
    </div>
    <p v-if="note" class="text-sm text-muted-foreground" :class="list.length ? 'mb-3' : ''">{{ note }}</p>
    <DataRegion
      v-if="!note || list.length"
      :is-first-load="placements.isFirstLoad.value"
      :error="placements.data.value ? null : placements.error.value"
      :has-data="Boolean(placements.data.value)"
      :is-empty="Boolean(placements.data.value) && !list.length"
      empty-title="Not placed on a server"
      loading-label="Reading placements"
      @retry="placements.refetch()"
    >
      <ul role="list" class="flex flex-col gap-2">
        <li v-for="(placement, index) in list" :key="placement.id">
          <details :open="startsOpen(placement, index)" class="group rounded-xl border border-border bg-card">
            <summary
              class="flex cursor-pointer list-none flex-wrap items-center gap-2 px-3 py-2 text-sm max-md:min-h-11 [&::-webkit-details-marker]:hidden"
            >
              <ChevronRight
                :size="14"
                class="shrink-0 text-muted-foreground transition-transform group-open:rotate-90 motion-reduce:transition-none"
                aria-hidden="true"
              />
              <StatusPill :state="InfraJobRules.state(placement.status)" :label="InfraJobRules.word(placement.status)" />
              <span class="font-medium">{{ serverName(placement.serverId) }}</span>
              <span class="text-xs text-muted-foreground"
                ><RelativeTime :value="placement.createdAt" /><template v-if="placement.actor">
                  · {{ placement.actor }}</template
                ></span
              >
            </summary>
            <div class="flex flex-col gap-3 border-t border-border px-3 py-3">
              <Alert
                v-if="placement.reason && InfraJobRules.canRetry(placement.status)"
                :severity="placement.status === 'interrupted' ? 'warning' : 'danger'"
                :description="placement.reason"
              />
              <JobSteps :steps="placement.steps" :label="`Placement steps on ${serverName(placement.serverId)}`" />
              <div v-if="placement.facts.databases.length" class="flex flex-wrap items-center gap-1.5 text-xs">
                <Database :size="14" class="text-muted-foreground" aria-hidden="true" />
                <span class="text-muted-foreground">Databases</span>
                <Badge v-for="name in placement.facts.databases" :key="name" variant="outline" size="sm">{{ name }}</Badge>
              </div>
              <ul v-if="placement.facts.sites.length" role="list" class="flex flex-col gap-1.5" aria-label="Placed sites">
                <li
                  v-for="site in PlacementRules.placedSites(placement)"
                  :key="`${site.site}-${site.environment}`"
                  class="flex flex-wrap items-center gap-x-2 gap-y-1 text-xs"
                >
                  <span class="font-medium">{{ site.site }}</span>
                  <Badge variant="neutral" size="sm">{{ site.environment }}</Badge>
                  <span class="font-mono">{{ site.node }}</span>
                  <span v-if="site.tailnetAddress" class="font-mono text-muted-foreground">{{ site.tailnetAddress }}</span>
                  <Badge v-if="site.online !== undefined" :variant="site.online ? 'success' : 'neutral'" size="sm">{{
                    site.online ? "Online" : "Offline"
                  }}</Badge>
                  <Badge v-else-if="!site.joined" variant="warning" size="sm">Not joined</Badge>
                  <a
                    v-if="site.url"
                    :href="site.url"
                    target="_blank"
                    rel="noopener noreferrer"
                    class="inline-flex min-h-8 items-center gap-1 text-primary hover:underline max-md:min-h-11 sm:ms-auto"
                    >{{ site.url.replace(/^https?:\/\//, "") }}<ExternalLink :size="12" aria-hidden="true"
                  /></a>
                </li>
              </ul>
            </div>
          </details>
        </li>
      </ul>
    </DataRegion>
    <PlacementModal
      v-if="plan && placeable"
      :open="placing"
      :product="props.product"
      :plan="plan"
      :servers="servers.data.value ?? []"
      :placements="list"
      :operations="operations"
      @update:open="placing = $event"
    />
  </section>
</template>
