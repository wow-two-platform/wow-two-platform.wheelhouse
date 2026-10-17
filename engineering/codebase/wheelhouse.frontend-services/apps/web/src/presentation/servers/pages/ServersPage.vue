<script setup lang="ts">
import { computed, ref } from 'vue';
import { RouterLink } from 'vue-router';
import { ExternalLink, KeyRound, Pencil, Plus, Server as ServerIcon } from 'lucide-vue-next';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import {
  Badge,
  RelativeTime,
  Table,
  TableHead,
  TableBody,
  TableRow,
  TableCell,
  TableHeaderCell,
  Text,
} from '@wow-two-beta/fe/presentation/display';
import { DataRegion, useSkeletonItems } from '@wow-two-beta/fe/presentation/feedback';
import {
  SelectPicker,
  SelectPickerTrigger,
  SelectPickerValue,
  SelectPickerContent,
  SelectPickerItem,
} from '@wow-two-beta/fe/presentation/forms';
import { PageHeaderActions } from '@wow-two-beta/fe/presentation/layout';
import {
  hostedProductsOf,
  useServerChanges,
  useServers,
  useServerVitals,
  useVitalsHistory,
  type HostedProduct,
} from '@/application/servers';
import { useVaultDefinitions } from '@/application/secrets';
import { useDeploymentTargets } from '@/application/deployments';
import { productLocation, vaultLocation, type ConsoleLocation } from '@/application/navigation';
import { useProducts } from '@/application/products';
import { useRefresh } from '@/bootstrap/query';
import {
  ServerExtensions,
  TrendRanges,
  VpsProvider,
  VpsProviderLabels,
  hostTrend,
  type Server,
  type TrendRange,
} from '@/domain/servers';
import type { VaultDefinition } from '@/domain/secrets';
import { Measures } from '@/domain/common';
import { AttentionRules, type AttentionItem } from '@/domain/overview';
import { Panel, RefreshButton } from '@/presentation/common/components';
import { ProductIcon } from '@/presentation/products';
import PortfolioAttention from '../components/PortfolioAttention.vue';
import ResourceMeter from '../components/ResourceMeter.vue';
import TrendLine from '../components/TrendLine.vue';
import ServerFormModal from '../components/ServerFormModal.vue';
import VaultFormModal from '../components/VaultFormModal.vue';

/**
 * Presents the inventory's servers: the products each hosts, their vaults and their current, timestamped resource
 * readings. A server that cannot be read, or runs overloaded, names the products it affects; each opens its page.
 */
defineOptions({ name: 'ServersPage' });
const servers = useServers();
const products = useProducts();
const serverChanges = useServerChanges();
const vaults = useVaultDefinitions();
const serverForm = ref<{ open: boolean; server: Server | null }>({ open: false, server: null });
const vaultForm = ref<{ open: boolean; server: string; vault: VaultDefinition | null }>({
  open: false,
  server: '',
  vault: null,
});
/** The vaults that run on one server. */
const vaultsOn = (server: string) => vaults.vaults.value.filter((vault) => vault.server === server);
const vitals = useServerVitals();
const targets = useDeploymentTargets();
/** Each target opens on its product's page, in its environment, whose catalog slug the target names. */
const productLinks = computed(() => {
  const links = new Map<string, ConsoleLocation>();
  if (targets.loading.value || targets.error.value) return links;
  for (const target of targets.data.value ?? [])
    links.set(target.id, productLocation(target.product, { env: target.environment, inspect: 'target' }));
  return links;
});
const refresh = useRefresh(() => Promise.all([servers.refetch(), vitals.refetch(), targets.refetch()]));
/** The products a server hosts, from the targets that place a product environment on it. */
const hostedOf = (server: Server | undefined): HostedProduct[] =>
  server ? hostedProductsOf(server.slug, targets.data.value ?? [], products.products.value) : [];
/** Why a server is degraded for the products it hosts: unreachable, overloaded, short on memory or disk. */
const impactOf = (server: Server | undefined): AttentionItem[] =>
  server && vitals.data.value ? AttentionRules.ofServer(vitals.data.value, server.slug) : [];
/** The impact's tone: danger when anything is unreadable or full, warning for load alone. */
const impactTone = (items: readonly AttentionItem[]) => (items.some((item) => item.tone === 'danger') ? 'danger' : 'warning');
const provider = ref<string | null>(null);
/** The servers the provider filter keeps, degraded ones first — unreadable or full before overloaded — else in order. */
const visible = computed(() => {
  const rank = (server: Server) => {
    const items = impactOf(server);
    return items.length ? (impactTone(items) === 'danger' ? 0 : 1) : 2;
  };
  return (servers.data.value ?? [])
    .filter((server) => !provider.value || server.provider === provider.value)
    .map((server, index) => ({ server, index, rank: rank(server) }))
    .sort((left, right) => left.rank - right.rank || left.index - right.index)
    .map((entry) => entry.server);
});
/** The servers, or placeholder panels until the first inventory read arrives. */
const serverRows = useSkeletonItems(() => (servers.data.value ? visible.value : undefined), 2);
/** The environment bindings, or placeholder rows until the first read arrives. */
const targetRows = useSkeletonItems(() => targets.data.value, 3);
const groups = computed(() => ServerExtensions.byServer(vitals.data.value?.targets ?? []));
/** One server's own host readings, whether or not it runs a target; none for a placeholder panel. */
const hostOf = (server: Server | undefined) =>
  server ? ServerExtensions.hostOf(vitals.data.value, server.slug) : null;
/** Why one server's host could not be read, when the pass says. */
const hostReason = (server: Server | undefined) =>
  server ? (ServerExtensions.readingOf(vitals.data.value, server.slug)?.reason ?? null) : null;
/** The vaults on one server, or a placeholder row until the first vault catalog arrives. */
const vaultRows = (server: Server | undefined): ReadonlyArray<VaultDefinition | undefined> =>
  vaults.loading.value || !server ? [undefined] : vaultsOn(server.slug);
const TrendLabels: Record<TrendRange, string> = { day: 'Last 24 hours', week: 'Last 7 days', month: 'Last 30 days' };
const trendRange = ref<TrendRange>('day');
const history = useVitalsHistory(() => TrendRanges[trendRange.value]);
/** The trend window, fixed per read so the lines line up across servers. */
const trendWindow = computed(() => {
  const until = history.data.value ? Date.now() : 0;
  return { since: until - TrendRanges[trendRange.value] * 3_600_000, until };
});
</script>
<template>
  <div class="space-y-6">
    <PageHeaderActions>
      <RefreshButton size="md" :refreshing="refresh.refreshing.value" @refresh="refresh.refresh" />
      <Button :is-disabled="serverForm.open" @click="serverForm = { open: true, server: null }">
        <template #leading><Plus :size="16" /></template>Add server
      </Button>
    </PageHeaderActions>
    <PortfolioAttention />
    <div class="flex flex-wrap items-center justify-between gap-3">
      <p class="text-sm text-muted-foreground">
        <template v-if="vitals.data.value"
          >Read <RelativeTime :value="vitals.data.value.collectedAt" is-live /> · updates every minute</template
        >
        <template v-else>Current resource readings from your configured hosts.</template>
      </p>
      <SelectPicker
        v-model="provider"
        is-clearable
        clear-label="All providers"
        :get-option-label="(value) => VpsProviderLabels[value as VpsProvider]"
        ><SelectPickerTrigger aria-label="Provider" width="12rem"
          ><SelectPickerValue placeholder="All providers" /></SelectPickerTrigger
        ><SelectPickerContent
          ><SelectPickerItem
            v-for="value in Object.values(VpsProvider)"
            :key="value"
            :item-key="value"
            :label="VpsProviderLabels[value]" /></SelectPickerContent
      ></SelectPicker>
    </div>
    <p
      v-if="vitals.error.value"
      role="alert"
      class="rounded-xl bg-destructive-soft p-4 text-sm text-destructive-soft-foreground"
    >
      {{ vitals.error.value.message }} Last available readings remain visible.
    </p>
    <DataRegion
      :is-first-load="servers.isFirstLoad.value"
      :is-refreshing="servers.isRefreshing.value"
      :error="servers.error.value"
      :has-data="Boolean(servers.data.value)"
      :is-empty="Boolean(servers.data.value) && !visible.length"
      empty-title="No servers"
      :empty-description="provider ? 'No server matches this provider.' : 'Add one to deploy to it.'"
      loading-label="Loading servers"
    >
      <div class="grid items-start gap-5 2xl:grid-cols-2">
        <Panel
          v-for="(server, index) in serverRows"
          :key="server?.slug ?? `placeholder-${index}`"
          :title="server?.name ?? 'Server'"
          :description="
            server
              ? `${server.host} · ${server.region} · SSH ${server.sshUser}${server.sshPort === 22 ? '' : `:${server.sshPort}`}`
              : 'host · region · SSH user'
          "
        >
          <template #actions>
            <Badge>{{ server ? VpsProviderLabels[server.provider] : 'Provider' }}</Badge>
            <Button
              size="sm"
              shape="square"
              variant="ghost"
              tone="neutral"
              :aria-label="`Edit ${server?.name ?? 'server'}`"
              @click="server && (serverForm = { open: true, server })"
            >
              <Pencil :size="14" />
            </Button>
          </template>
          <!-- A degraded server leads with what is wrong and the products that feel it. -->
          <div
            v-if="impactOf(server).length"
            role="alert"
            class="mb-5 rounded-xl p-4"
            :class="
              impactTone(impactOf(server)) === 'danger'
                ? 'bg-destructive-soft text-destructive-soft-foreground'
                : 'bg-warning-soft text-warning-soft-foreground'
            "
          >
            <p v-for="item in impactOf(server)" :key="item.id" class="text-sm">
              <span class="font-semibold">{{ item.title }}.</span> {{ item.detail }}
            </p>
            <template v-if="hostedOf(server).length">
              <p class="mt-3 text-xs font-semibold">Affects</p>
              <ul role="list" class="mt-1.5 flex flex-wrap gap-1.5">
                <li v-for="item in hostedOf(server)" :key="item.slug">
                  <RouterLink
                    :to="productLocation(item.slug)"
                    class="inline-flex min-h-8 items-center gap-1.5 rounded-lg bg-background/70 px-2 py-1 text-xs font-medium text-foreground hover:bg-background focus-visible:outline-2 focus-visible:outline-ring max-md:min-h-11"
                    ><ProductIcon :product-slug="item.slug" :name="item.name" size="xs" />{{ item.name
                    }}<span class="font-normal text-muted-foreground">{{ item.environments.join(' · ') }}</span></RouterLink
                  >
                </li>
              </ul>
            </template>
          </div>
          <DataRegion :is-first-load="vitals.isFirstLoad.value" loading-label="Reading host vitals" class="mb-5">
            <div v-if="hostOf(server) || !server || vitals.isFirstLoad.value" class="grid gap-3 sm:grid-cols-2">
              <ResourceMeter
                label="CPU load"
                :value="hostOf(server) ? ServerExtensions.loadPercent(hostOf(server)!) : null"
                :refreshing="refresh.refreshing.value"
              />
              <ResourceMeter
                label="Memory"
                :value="hostOf(server) ? ServerExtensions.memoryPercent(hostOf(server)!) : null"
                :refreshing="refresh.refreshing.value"
              />
              <ResourceMeter
                v-for="disk in hostOf(server)?.disks"
                :key="disk.path"
                :label="`Disk · ${disk.path}`"
                :value="ServerExtensions.diskPercent(disk)"
                :detail="`${Measures.bytes(disk.freeBytes)} free of ${Measures.bytes(disk.totalBytes)}`"
                :refreshing="refresh.refreshing.value"
              />
            </div>
            <Text v-else class="text-sm text-muted-foreground"
              >Host vitals are unavailable{{ hostReason(server) ? `: ${hostReason(server)}` : '' }}.</Text
            >
          </DataRegion>
          <section class="mb-5" :aria-label="`${server?.name ?? 'Server'} products`">
            <h3 class="mb-2 text-xs font-medium text-muted-foreground">Products</h3>
            <DataRegion :is-first-load="targets.isFirstLoad.value || products.isFirstLoad.value" loading-label="Reading products">
              <ul v-if="hostedOf(server).length" role="list" class="flex flex-wrap gap-2">
                <li v-for="item in hostedOf(server)" :key="item.slug">
                  <RouterLink
                    :to="productLocation(item.slug)"
                    class="inline-flex min-h-9 items-center gap-2 rounded-xl border border-border bg-background/40 px-2.5 py-1.5 text-sm font-medium transition-colors hover:border-primary/40 focus-visible:outline-2 focus-visible:outline-ring max-md:min-h-11"
                    :class="item.active ? '' : 'opacity-60'"
                    ><ProductIcon :product-slug="item.slug" :name="item.name" size="xs" />{{ item.name
                    }}<span class="text-xs font-normal text-muted-foreground">{{ item.environments.join(' · ') }}</span></RouterLink
                  >
                </li>
              </ul>
              <p v-else class="text-sm text-muted-foreground">No product runs here yet.</p>
            </DataRegion>
          </section>
          <section class="mb-5" :aria-label="`${server?.name ?? 'Server'} trends`">
            <div class="mb-2 flex flex-wrap items-center justify-between gap-2">
              <h3 class="text-xs font-medium text-muted-foreground">Trends · sampled every few minutes</h3>
              <SelectPicker v-model="trendRange" :get-option-label="(value) => TrendLabels[value as TrendRange]"
                ><SelectPickerTrigger aria-label="Trend range" width="9rem"><SelectPickerValue /></SelectPickerTrigger
                ><SelectPickerContent
                  ><SelectPickerItem
                    v-for="(label, value) in TrendLabels"
                    :key="value"
                    :item-key="value"
                    :value="value"
                    :label="label" /></SelectPickerContent
              ></SelectPicker>
            </div>
            <p v-if="history.error.value" class="text-xs text-muted-foreground">
              Trends are unavailable: {{ history.error.value.message }}
            </p>
            <DataRegion
              v-else
              :is-first-load="history.isFirstLoad.value"
              loading-label="Reading trends"
              class="grid gap-3 sm:grid-cols-3"
            >
              <TrendLine
                label="CPU load"
                :points="server ? hostTrend(history.data.value ?? [], server.slug, 'loadPercent') : []"
                v-bind="trendWindow"
              />
              <TrendLine
                label="Memory"
                :points="server ? hostTrend(history.data.value ?? [], server.slug, 'memoryPercent') : []"
                v-bind="trendWindow"
              />
              <TrendLine
                label="Fullest disk"
                :points="server ? hostTrend(history.data.value ?? [], server.slug, 'diskPercent') : []"
                v-bind="trendWindow"
              />
            </DataRegion>
          </section>
          <section class="mb-5" :aria-label="`${server?.name ?? 'Server'} vaults`">
            <div class="mb-2 flex items-center justify-between gap-2">
              <h3 class="text-xs font-medium text-muted-foreground">Vaults</h3>
              <Button
                size="sm"
                variant="ghost"
                @click="server && (vaultForm = { open: true, server: server.slug, vault: null })"
              >
                <template #leading><Plus :size="14" /></template>Add vault
              </Button>
            </div>
            <DataRegion :is-first-load="vaults.loading.value" loading-label="Reading vaults">
              <ul v-if="vaultRows(server).length" class="divide-y divide-border rounded-xl border border-border">
                <li
                  v-for="(vault, row) in vaultRows(server)"
                  :key="vault?.slug ?? `placeholder-${row}`"
                  class="flex items-center gap-3 p-3 text-sm"
                >
                  <KeyRound :size="14" class="shrink-0 text-muted-foreground" />
                  <Text as="span" class="text-sm font-medium">{{ vault?.name }}</Text>
                  <Text as="span" class="min-w-0 truncate font-mono text-xs text-muted-foreground" skeleton-width="12rem">{{
                    vault?.url
                  }}</Text>
                  <Button v-if="vault" as-child class="ml-auto" size="sm" variant="ghost" tone="neutral">
                    <RouterLink :to="vaultLocation(vault.slug)" :title="`Administer ${vault.name}'s namespaces`"
                      ><ExternalLink :size="14" />Secrets</RouterLink
                    >
                  </Button>
                  <Button
                    :class="vault ? '' : 'ml-auto'"
                    size="sm"
                    shape="square"
                    variant="ghost"
                    tone="neutral"
                    :aria-label="`Edit ${vault?.name ?? 'vault'}`"
                    @click="server && vault && (vaultForm = { open: true, server: server.slug, vault })"
                  >
                    <Pencil :size="14" />
                  </Button>
                </li>
              </ul>
              <p v-else class="text-sm text-muted-foreground">No vault.</p>
            </DataRegion>
          </section>
          <template v-if="server">
            <section
              v-for="target in groups.get(server.slug) ?? []"
              :key="target.targetId"
              class="mt-4 border-t border-border pt-4"
            >
              <div class="mb-3 flex flex-wrap items-center gap-2">
                <ServerIcon :size="15" />
                <h3 class="text-sm font-medium">{{ target.targetId }}</h3>
                <span class="ml-auto text-xs text-muted-foreground">{{ target.release ?? 'Release unknown' }}</span>
              </div>
              <p v-if="!target.ok" role="alert" class="text-sm text-destructive">
                {{ target.reason ?? 'This target could not be read.' }}
              </p>
              <template v-else>
                <p v-for="problem in target.problems" :key="problem" class="mb-2 text-sm text-warning">{{ problem }}</p>
                <div
                  v-for="container in target.containers ?? []"
                  :key="container.service"
                  class="mb-2 flex flex-wrap items-center justify-between gap-3 rounded-lg bg-muted/40 p-3 text-sm"
                >
                  <div>
                    <p class="font-medium">{{ container.service }}</p>
                    <p class="mt-1 text-xs text-muted-foreground">
                      {{ ServerExtensions.containerLabel(container) }} · {{ container.restarts }} restarts · started
                      <RelativeTime v-if="container.startedAt" :value="container.startedAt" /><template v-else
                        >—</template
                      >
                    </p>
                  </div>
                  <div class="text-right text-xs tabular-nums">
                    <p>
                      {{ container.cpuPercent == null ? 'CPU unavailable' : `${container.cpuPercent.toFixed(1)}% CPU` }}
                    </p>
                    <p class="mt-1 text-muted-foreground">
                      {{ Measures.bytes(container.memoryBytes) }} / {{ Measures.bytes(container.memoryLimitBytes) }}
                    </p>
                  </div>
                </div>
                <p v-if="target.containers == null" class="text-sm text-muted-foreground">
                  Container readings are unavailable.
                </p>
                <p v-else-if="!target.containers.length" class="text-sm text-muted-foreground">No containers observed.</p>
              </template>
            </section>
          </template>
        </Panel>
      </div>
    </DataRegion>
    <Panel title="Environment bindings" description="Each environment connects one product to one configured host.">
      <DataRegion
        :is-first-load="targets.isFirstLoad.value"
        :is-refreshing="targets.isRefreshing.value"
        :error="targets.error.value"
        :has-data="Boolean(targets.data.value)"
        :is-empty="Boolean(targets.data.value) && !targets.data.value?.length"
        empty-title="No deployment targets"
        loading-label="Loading environment bindings"
      >
        <div class="overflow-x-auto">
          <Table density="compact"
            ><TableHead
              ><TableRow
                ><TableHeaderCell>Environment</TableHeaderCell><TableHeaderCell>Product</TableHeaderCell
                ><TableHeaderCell>Host</TableHeaderCell><TableHeaderCell>Provider</TableHeaderCell></TableRow
              ></TableHead
            ><TableBody
              ><TableRow v-for="(target, index) in targetRows" :key="target?.id ?? `placeholder-${index}`"
                ><TableCell
                  ><div v-if="target" class="min-w-0">
                    <RouterLink
                      v-if="productLinks.has(target.id)"
                      :to="productLinks.get(target.id)!"
                      class="font-medium text-primary hover:underline"
                      >{{ target.environment }}</RouterLink
                    >
                    <span v-else class="font-medium">{{ target.environment }}</span>
                    <p class="text-xs text-muted-foreground">{{ target.id }}</p>
                  </div></TableCell
                ><TableCell>{{ target?.product }}</TableCell
                ><TableCell>{{ target?.host }}</TableCell
                ><TableCell>{{
                  target ? (VpsProviderLabels[target.provider as VpsProvider] ?? target.provider) : ''
                }}</TableCell></TableRow
              ></TableBody
            ></Table
          >
        </div>
      </DataRegion>
    </Panel>
    <ServerFormModal
      :open="serverForm.open"
      :server="serverForm.server"
      :operations="serverChanges"
      @update:open="(open) => (serverForm = { ...serverForm, open })"
    />
    <VaultFormModal
      :open="vaultForm.open"
      :server="vaultForm.server"
      :vault="vaultForm.vault"
      :operations="vaults"
      @update:open="(open) => (vaultForm = { ...vaultForm, open })"
    />
  </div>
</template>
