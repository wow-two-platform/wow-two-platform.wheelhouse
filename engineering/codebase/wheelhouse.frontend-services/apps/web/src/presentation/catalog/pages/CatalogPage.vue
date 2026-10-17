<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { Download, ExternalLink } from 'lucide-vue-next';
import { NoticeTone, notify } from '@wow-two-beta/fe/feedback';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { Badge, DataTable, Text, type DataTableColumn } from '@wow-two-beta/fe/presentation/display';
import { DataRegion } from '@wow-two-beta/fe/presentation/feedback';
import { PageHeaderActions } from '@wow-two-beta/fe/presentation/layout';
import { useCatalog } from '@/application/catalog';
import { useWorkspaces } from '@/application/workspaces';
import { useRefresh } from '@/bootstrap/query';
import { CatalogEntryState, CatalogRules, type CatalogEntry } from '@/domain/catalog';
import { failureReason } from '@/integration/common';
import { Panel, RefreshButton } from '@/presentation/common/components';

/** @internal Each entry state's badge. */
const States = {
  [CatalogEntryState.New]: { label: 'New', variant: 'info' },
  [CatalogEntryState.InWheelhouse]: { label: 'In Wheelhouse', variant: 'success' },
  [CatalogEntryState.External]: { label: 'Not deployed here', variant: 'neutral' },
} as const;

/** @internal The table's columns; the `cell` slot renders each. */
const Columns: DataTableColumn<CatalogEntry>[] = [
  { key: 'app', header: 'App', accessor: (entry) => entry.name, isSortable: true },
  { key: 'workspace', header: 'Workspace', accessor: (entry) => entry.workspace, isSortable: true },
  { key: 'kind', header: 'Kind', accessor: (entry) => entry.kind, isSortable: true },
  { key: 'state', header: 'State', accessor: (entry) => CatalogRules.stateOf(entry), isSortable: true },
];

/**
 * Every wow2 app the workspace's `catalog.yml` lists, against Wheelhouse: the ones it deploys and has not imported can
 * be selected and imported, which creates their products and any workspace they need.
 */
defineOptions({ name: 'CatalogPage' });
const { catalog, importing, importEntries } = useCatalog();
const workspaces = useWorkspaces();
const refresh = useRefresh(() => catalog.refetch());
const selected = ref<string[]>([]);
const entries = computed(() => catalog.data.value?.entries ?? []);
const importable = computed(() => CatalogRules.importable(entries.value));
const selection = computed(() => CatalogRules.selection(entries.value, selected.value));
const known = computed(() => new Set(workspaces.workspaces.value.map((workspace) => workspace.slug)));
const source = computed(() => catalog.data.value?.source ?? null);
/** The catalog file on GitHub at the commit it was read from, when the source names a GitHub repository. */
const sourceHref = computed(() =>
  source.value && /^[\w.-]+\/[\w.-]+$/.test(source.value.repository)
    ? `https://github.com/${source.value.repository}/blob/${source.value.commit ?? source.value.ref}/${source.value.path}`
    : null,
);

/* A reload drops selected entries that were imported meanwhile or left the catalog. */
watch(entries, () => (selected.value = CatalogRules.selection(entries.value, selected.value)));

/** The catalog workspace's name for an entry's slug. @internal */
function workspaceName(slug: string): string {
  return catalog.data.value?.workspaces.find((workspace) => workspace.slug === slug)?.name ?? slug;
}

/** Keeps only the importable keys the table hands back. @internal */
function select(keys: readonly unknown[]): void {
  selected.value = CatalogRules.selection(entries.value, keys);
}

/** Imports the selection and reports what changed. @internal */
async function runImport(): Promise<void> {
  const slugs = selection.value;
  if (!slugs.length || importing.value) return;
  const result = await importEntries(slugs);
  if (result.ok) {
    const warnings = CatalogRules.warningLine(result.value);
    notify({
      tone: warnings ? NoticeTone.Warning : NoticeTone.Success,
      title: 'Catalog imported',
      description: warnings ? `${CatalogRules.summary(result.value)} ${warnings}.` : CatalogRules.summary(result.value),
    });
    selected.value = [];
  } else {
    notify({ tone: NoticeTone.Danger, title: 'The import did not run', description: failureReason(result.failure) });
  }
}
</script>
<template>
  <div class="flex flex-col gap-6">
    <PageHeaderActions>
      <RefreshButton size="md" icon-only :refreshing="refresh.refreshing.value" @refresh="refresh.refresh" />
      <Button :is-disabled="!selection.length" :is-loading="importing" @click="runImport">
        <template #leading><Download :size="16" /></template>Import{{ selection.length ? ` ${selection.length}` : '' }}
      </Button>
    </PageHeaderActions>
    <DataRegion
      :is-first-load="catalog.isFirstLoad.value"
      :is-refreshing="catalog.isRefreshing.value"
      :error="catalog.error.value"
      :has-data="Boolean(catalog.data.value)"
      :is-empty="Boolean(catalog.data.value) && !entries.length"
      empty-title="The catalog lists no apps"
      loading-label="Reading the catalog"
      class="flex flex-col gap-6"
      @retry="refresh.refresh"
    >
      <section aria-label="Source" class="flex flex-wrap items-center gap-x-6 gap-y-2 text-sm">
        <span class="flex min-w-0 items-center gap-2">
          <span class="text-muted-foreground">Source</span>
          <Text as="span" class="min-w-0 truncate font-mono text-xs" skeleton-width="14rem">
            <a
              v-if="source && sourceHref"
              :href="sourceHref"
              target="_blank"
              rel="noopener noreferrer"
              class="inline-flex items-center gap-1 hover:text-primary hover:underline"
              >{{ CatalogRules.sourceLabel(source) }}<ExternalLink :size="12"
            /></a>
            <template v-else-if="source">{{ CatalogRules.sourceLabel(source) }}</template>
          </Text>
        </span>
        <Text as="span" class="text-muted-foreground" skeleton-width="10rem"
          ><template v-if="catalog.data.value"
            >{{ entries.length }} apps · {{ importable.length }} to import</template
          ></Text
        >
      </section>

      <section aria-labelledby="catalog-workspaces" class="min-w-0">
        <h2 id="catalog-workspaces" class="mb-2 text-sm font-semibold">Workspaces</h2>
        <ul role="list" class="flex flex-wrap gap-2">
          <li
            v-for="workspace in catalog.data.value?.workspaces ?? []"
            :key="workspace.slug"
            class="flex items-center gap-2 rounded-xl border border-border bg-card px-3 py-2 text-sm"
            :title="workspace.description || undefined"
          >
            <span class="font-medium">{{ workspace.name }}</span>
            <span class="font-mono text-xs text-muted-foreground">{{ workspace.slug }}</span>
            <Badge v-if="!known.has(workspace.slug)" size="sm" variant="info">New</Badge>
          </li>
        </ul>
      </section>

      <Panel title="Apps">
        <template #actions>
          <Button
            size="sm"
            variant="outline"
            tone="neutral"
            :is-disabled="!importable.length"
            @click="select(selection.length === importable.length ? [] : importable)"
            >{{ selection.length === importable.length && importable.length ? 'Clear selection' : `Select all new (${importable.length})` }}</Button
          >
        </template>
        <DataTable
          :columns="Columns"
          :data="entries"
          :row-key="(entry) => entry.slug"
          selection-mode="multiple"
          :selection="selection"
          :is-row-selectable="(entry) => CatalogRules.isImportable(entry)"
          :skeleton-rows="4"
          density="compact"
          empty-content="The catalog lists no apps."
          @update:selection="select"
        >
          <template #cell="{ row, column }">
            <template v-if="column.key === 'app'">
              <span class="block font-medium">{{ row.name }}</span>
              <span class="block font-mono text-xs text-muted-foreground">{{ row.slug }}</span>
            </template>
            <template v-else-if="column.key === 'workspace'">{{ workspaceName(row.workspace) }}</template>
            <span v-else-if="column.key === 'kind'" class="capitalize">{{ row.kind }}</span>
            <Badge
              v-else-if="column.key === 'state'"
              size="sm"
              :variant="States[CatalogRules.stateOf(row)].variant"
              :title="CatalogRules.stateOf(row) !== 'external' ? undefined : row.deploy === 'none' ? 'Not deployed' : `Deployed by ${row.deploy}`"
              >{{ States[CatalogRules.stateOf(row)].label }}</Badge
            >
          </template>
        </DataTable>
      </Panel>
    </DataRegion>
  </div>
</template>
