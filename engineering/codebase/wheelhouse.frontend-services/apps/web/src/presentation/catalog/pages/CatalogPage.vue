<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { Download, ExternalLink } from 'lucide-vue-next';
import { NoticeTone, notify } from '@wow-two-beta/fe/feedback';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { Text } from '@wow-two-beta/fe/presentation/display';
import { DataRegion, useSkeletonItems } from '@wow-two-beta/fe/presentation/feedback';
import { PageHeaderActions } from '@wow-two-beta/fe/presentation/layout';
import { useCatalog } from '@/application/catalog';
import { useWorkspaces } from '@/application/workspaces';
import { useRefresh } from '@/bootstrap/query';
import { CatalogRules, type CatalogWorkspaceGroup } from '@/domain/catalog';
import { failureReason } from '@/integration/common';
import { RefreshButton } from '@/presentation/common/components';
import CatalogWorkspacePanel from '../components/CatalogWorkspacePanel.vue';

/**
 * Every wow2 app the workspace's `catalog.yml` lists, against Wheelhouse, grouped by catalog workspace. The apps it
 * deploys and has not imported can be selected one by one or a workspace at a time and imported together, or a whole
 * workspace imported at once; an import creates their products and any workspace they need.
 */
defineOptions({ name: 'CatalogPage' });
const { catalog, importing, importEntries } = useCatalog();
const workspaces = useWorkspaces();
const refresh = useRefresh(() => catalog.refetch());
const selected = ref<string[]>([]);
/** The catalog workspace whose own import runs; null while the page's selection imports, or nothing does. */
const importingWorkspace = ref<string | null>(null);
const entries = computed(() => catalog.data.value?.entries ?? []);
const importable = computed(() => CatalogRules.importable(entries.value));
const selection = computed(() => CatalogRules.selection(entries.value, selected.value));
const groups = computed(() => (catalog.data.value ? CatalogRules.groups(catalog.data.value) : undefined));
const panels = useSkeletonItems(() => groups.value, 2);
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

/** Replaces one workspace's part of the selection with the keys its table hands back. @internal */
function selectIn(group: CatalogWorkspaceGroup, keys: readonly unknown[]): void {
  const others = selection.value.filter((slug) => !group.importable.includes(slug));
  selected.value = CatalogRules.selection(entries.value, [...others, ...keys]);
}

function selectAllIn(group: CatalogWorkspaceGroup, isSelected: boolean): void {
  selected.value = CatalogRules.selectWorkspace(entries.value, selection.value, group, isSelected);
}

/** Imports the slugs and reports what changed; imported slugs leave the selection. @internal */
async function runImport(slugs: readonly string[], workspace: string | null = null): Promise<void> {
  if (!slugs.length || importing.value) return;
  importingWorkspace.value = workspace;
  try {
    const result = await importEntries(slugs);
    if (result.ok) {
      const warnings = CatalogRules.warningLine(result.value);
      notify({
        tone: warnings ? NoticeTone.Warning : NoticeTone.Success,
        title: workspace ? `Imported ${groups.value?.find((group) => group.slug === workspace)?.name ?? workspace}` : 'Catalog imported',
        description: warnings ? `${CatalogRules.summary(result.value)} ${warnings}.` : CatalogRules.summary(result.value),
      });
      selected.value = selection.value.filter((slug) => !slugs.includes(slug));
    } else {
      notify({ tone: NoticeTone.Danger, title: 'The import did not run', description: failureReason(result.failure) });
    }
  } finally {
    importingWorkspace.value = null;
  }
}
</script>
<template>
  <div class="flex flex-col gap-6">
    <PageHeaderActions>
      <RefreshButton size="md" icon-only :refreshing="refresh.refreshing.value" @refresh="refresh.refresh" />
      <Button :is-disabled="!selection.length" :is-loading="importing && importingWorkspace === null" @click="runImport(selection)">
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
            >{{ entries.length }} apps · {{ importable.length }} importable</template
          ></Text
        >
      </section>

      <CatalogWorkspacePanel
        v-for="(group, index) in panels"
        :key="group?.slug ?? `placeholder-${index}`"
        :group="group"
        :selected="selection"
        :is-known="group ? known.has(group.slug) : true"
        :is-importing="Boolean(group) && importingWorkspace === group?.slug"
        :is-busy="importing"
        @select="group && selectIn(group, $event)"
        @select-all="group && selectAllIn(group, $event)"
        @import="group && runImport(group.importable, group.slug)"
      />
    </DataRegion>
  </div>
</template>
