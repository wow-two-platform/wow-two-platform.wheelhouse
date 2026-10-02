<script setup lang="ts">
import { computed, ref } from 'vue';
import { RouterLink } from 'vue-router';
import { KeyRound, Pencil, Plus, Server as ServerIcon } from 'lucide-vue-next';
import { Button } from '@wow-two-beta/ui-vue/presentation/actions';
import {
  Badge,
  Table,
  TableHead,
  TableBody,
  TableRow,
  TableCell,
  TableHeaderCell,
} from '@wow-two-beta/ui-vue/presentation/display';
import {
  SelectPicker,
  SelectPickerTrigger,
  SelectPickerValue,
  SelectPickerContent,
  SelectPickerItem,
} from '@wow-two-beta/ui-vue/presentation/forms';
import { useServerChanges, useServers, useServerVitals, useVitalsHistory } from '@/application/servers';
import { useVaultDefinitions } from '@/application/secrets';
import { useDeploymentTargets } from '@/application/deployments';
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
import { Panel, LoadState, PageActions, RefreshButton } from '@/presentation/common/components';
import PortfolioAttention from '../components/PortfolioAttention.vue';
import ResourceMeter from '../components/ResourceMeter.vue';
import TrendLine from '../components/TrendLine.vue';
import ServerFormModal from '../components/ServerFormModal.vue';
import VaultFormModal from '../components/VaultFormModal.vue';

/** Presents the inventory's servers, their vaults and their current, timestamped resource readings. */
defineOptions({ name: 'ServersPage' });
const servers = useServers();
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
/** Each target opens in the workspace under its product, whose catalog slug the target names. */
const workspaceLinks = computed(() => {
  const links = new Map<string, { path: string; query: { product: string; target: string; inspect: string } }>();
  if (targets.loading.value || targets.error.value) return links;
  for (const target of targets.data.value ?? [])
    links.set(target.id, { path: '/', query: { product: target.product, target: target.id, inspect: 'target' } });
  return links;
});
const refresh = useRefresh(() => Promise.all([servers.refetch(), vitals.refetch(), targets.refetch()]));
const provider = ref<string | null>(null);
const visible = computed(() =>
  (servers.data.value ?? []).filter((server) => !provider.value || server.provider === provider.value),
);
const groups = computed(() => ServerExtensions.byServer(vitals.data.value?.targets ?? []));
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
    <PageActions>
      <RefreshButton size="md" :refreshing="refresh.refreshing.value" @refresh="refresh.refresh" />
      <Button :is-disabled="serverForm.open" @click="serverForm = { open: true, server: null }">
        <template #leading><Plus :size="16" /></template>Add server
      </Button>
    </PageActions>
    <PortfolioAttention />
    <div class="flex flex-wrap items-center justify-between gap-3">
      <p class="text-sm text-muted-foreground">
        {{
          vitals.data.value
            ? `Read ${Measures.moment(vitals.data.value.collectedAt)} · updates every minute`
            : 'Current resource readings from your configured hosts.'
        }}
      </p>
      <div class="w-48">
        <SelectPicker
          v-model="provider"
          is-clearable
          clear-label="All providers"
          :get-option-label="(value) => VpsProviderLabels[value as VpsProvider]"
          ><SelectPickerTrigger aria-label="Provider"
            ><SelectPickerValue placeholder="All providers" /></SelectPickerTrigger
          ><SelectPickerContent
            ><SelectPickerItem
              v-for="value in Object.values(VpsProvider)"
              :key="value"
              :item-key="value"
              :label="VpsProviderLabels[value]" /></SelectPickerContent
        ></SelectPicker>
      </div>
    </div>
    <p
      v-if="vitals.error.value"
      role="alert"
      class="rounded-xl bg-destructive-soft p-4 text-sm text-destructive-soft-foreground"
    >
      {{ vitals.error.value.message }} Last available readings remain visible.
    </p>
    <LoadState
      :loading="servers.loading.value"
      :error="servers.error.value"
      :has-data="Boolean(servers.data.value)"
      :empty="!visible.length"
      empty-title="No servers"
      :empty-description="provider ? 'No server matches this provider.' : 'Add one to deploy to it.'"
      @retry="servers.refetch"
    >
      <div class="grid items-start gap-5 2xl:grid-cols-2">
        <Panel
          v-for="server in visible"
          :key="server.slug"
          :title="server.name"
          :description="`${server.host} · ${server.region} · SSH ${server.sshUser}${server.sshPort === 22 ? '' : `:${server.sshPort}`}`"
        >
          <template #actions>
            <Badge>{{ VpsProviderLabels[server.provider] }}</Badge>
            <Button
              size="sm"
              variant="ghost"
              tone="neutral"
              :aria-label="`Edit ${server.name}`"
              @click="serverForm = { open: true, server }"
            >
              <Pencil :size="14" />
            </Button>
          </template>
          <div v-if="ServerExtensions.hostOf(groups.get(server.slug) ?? [])" class="mb-5 grid gap-3 sm:grid-cols-2">
            <ResourceMeter
              label="CPU load"
              :value="ServerExtensions.loadPercent(ServerExtensions.hostOf(groups.get(server.slug) ?? [])!)"
              :refreshing="refresh.refreshing.value"
            />
            <ResourceMeter
              label="Memory"
              :value="ServerExtensions.memoryPercent(ServerExtensions.hostOf(groups.get(server.slug) ?? [])!)"
              :refreshing="refresh.refreshing.value"
            />
            <ResourceMeter
              v-for="disk in ServerExtensions.hostOf(groups.get(server.slug) ?? [])?.disks"
              :key="disk.path"
              :label="`Disk · ${disk.path}`"
              :value="ServerExtensions.diskPercent(disk)"
              :detail="`${Measures.bytes(disk.freeBytes)} free of ${Measures.bytes(disk.totalBytes)}`"
              :refreshing="refresh.refreshing.value"
            />
          </div>
          <p v-else class="mb-5 text-sm text-muted-foreground">
            {{ vitals.loading.value ? 'Reading host vitals…' : 'Host vitals are unavailable.' }}
          </p>
          <section class="mb-5" :aria-label="`${server.name} trends`">
            <div class="mb-2 flex flex-wrap items-center justify-between gap-2">
              <h3 class="text-xs font-medium text-muted-foreground">Trends · sampled every few minutes</h3>
              <div class="w-36">
                <SelectPicker v-model="trendRange"
                  ><SelectPickerTrigger aria-label="Trend range"><SelectPickerValue /></SelectPickerTrigger
                  ><SelectPickerContent
                    ><SelectPickerItem
                      v-for="(label, value) in TrendLabels"
                      :key="value"
                      :item-key="value"
                      :value="value"
                      :label="label" /></SelectPickerContent
                ></SelectPicker>
              </div>
            </div>
            <p v-if="history.error.value" class="text-xs text-muted-foreground">
              Trends are unavailable: {{ history.error.value.message }}
            </p>
            <div v-else class="grid gap-3 sm:grid-cols-3">
              <TrendLine
                label="CPU load"
                :points="hostTrend(history.data.value ?? [], server.slug, 'loadPercent')"
                v-bind="trendWindow"
              />
              <TrendLine
                label="Memory"
                :points="hostTrend(history.data.value ?? [], server.slug, 'memoryPercent')"
                v-bind="trendWindow"
              />
              <TrendLine
                label="Fullest disk"
                :points="hostTrend(history.data.value ?? [], server.slug, 'diskPercent')"
                v-bind="trendWindow"
              />
            </div>
          </section>
          <section class="mb-5" :aria-label="`${server.name} vaults`">
            <div class="mb-2 flex items-center justify-between gap-2">
              <h3 class="text-xs font-medium text-muted-foreground">Vaults</h3>
              <Button size="sm" variant="ghost" @click="vaultForm = { open: true, server: server.slug, vault: null }">
                <template #leading><Plus :size="14" /></template>Add vault
              </Button>
            </div>
            <ul v-if="vaultsOn(server.slug).length" class="divide-y divide-border rounded-xl border border-border">
              <li v-for="vault in vaultsOn(server.slug)" :key="vault.slug" class="flex items-center gap-3 p-3 text-sm">
                <KeyRound :size="14" class="shrink-0 text-muted-foreground" />
                <span class="font-medium">{{ vault.name }}</span>
                <span class="min-w-0 truncate font-mono text-xs text-muted-foreground">{{ vault.url }}</span>
                <Button
                  class="ml-auto"
                  size="sm"
                  variant="ghost"
                  tone="neutral"
                  :aria-label="`Edit ${vault.name}`"
                  @click="vaultForm = { open: true, server: server.slug, vault }"
                >
                  <Pencil :size="14" />
                </Button>
              </li>
            </ul>
            <p v-else class="text-sm text-muted-foreground">{{ vaults.loading.value ? 'Reading vaults…' : 'No vault.' }}</p>
          </section>
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
                    {{ Measures.moment(container.startedAt) }}
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
        </Panel>
      </div>
    </LoadState>
    <Panel title="Environment bindings" description="Each environment connects one product to one configured host.">
      <LoadState
        :loading="targets.loading.value"
        :error="targets.error.value"
        :has-data="Boolean(targets.data.value)"
        :empty="!targets.data.value?.length"
        empty-title="No deployment targets"
        @retry="targets.refetch"
      >
        <div class="overflow-x-auto">
          <Table density="compact"
            ><TableHead
              ><TableRow
                ><TableHeaderCell>Environment</TableHeaderCell><TableHeaderCell>Product</TableHeaderCell
                ><TableHeaderCell>Host</TableHeaderCell><TableHeaderCell>Provider</TableHeaderCell></TableRow
              ></TableHead
            ><TableBody
              ><TableRow v-for="target in targets.data.value" :key="target.id"
                ><TableCell
                  ><RouterLink
                    v-if="workspaceLinks.has(target.id)"
                    :to="workspaceLinks.get(target.id)!"
                    class="font-medium text-primary hover:underline"
                    >{{ target.environment }}</RouterLink
                  >
                  <span v-else class="font-medium">{{ target.environment }}</span>
                  <p class="text-xs text-muted-foreground">{{ target.id }}</p></TableCell
                ><TableCell>{{ target.product }}</TableCell
                ><TableCell>{{ target.host }}</TableCell
                ><TableCell>{{ VpsProviderLabels[target.provider as VpsProvider] ?? target.provider }}</TableCell></TableRow
              ></TableBody
            ></Table
          >
        </div>
      </LoadState>
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
