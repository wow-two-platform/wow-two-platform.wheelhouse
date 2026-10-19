<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { Download, ExternalLink } from 'lucide-vue-next';
import { NoticeTone, notify } from '@wow-two-beta/fe/feedback';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { Heading, Text } from '@wow-two-beta/fe/presentation/display';
import { Alert, DataRegion, useSkeletonItems } from '@wow-two-beta/fe/presentation/feedback';
import { PageHeaderActions } from '@wow-two-beta/fe/presentation/layout';
import { useCatalog } from '@/application/catalog';
import { useWorkspaces } from '@/application/workspaces';
import { useRefresh } from '@/bootstrap/query';
import { CatalogRules, type CatalogWorkspaceGroup } from '@/domain/catalog';
import { failureReason } from '@/integration/common';
import { RefreshButton } from '@/presentation/common/components';
import CatalogWorkspacePanel from '../components/CatalogWorkspacePanel.vue';

/**
 * Every app the hubs' `catalog.yml` files list — the wow2 workspace, the ventures hub and the Ocharo hub — against
 * Wheelhouse, grouped by hub and then by catalog workspace. Each hub names the commit it was read at; a hub that could
 * not be read shows why, and the others still list theirs. The apps Wheelhouse deploys and has not imported can be
 * selected one by one or a workspace at a time and imported together, or a whole workspace imported at once; an import
 * creates their products and any workspace they need.
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
const hubs = computed(() => (catalog.data.value ? CatalogRules.sources(catalog.data.value) : undefined));
const hubSlots = useSkeletonItems(() => hubs.value, 1);
const known = computed(() => new Set(workspaces.workspaces.value.map((workspace) => workspace.slug)));
/** Two placeholder panels while a hub's workspaces are first read. @internal */
const placeholders = [undefined, undefined] as const;

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
async function runImport(slugs: readonly string[], workspace: CatalogWorkspaceGroup | null = null): Promise<void> {
  if (!slugs.length || importing.value) return;
  importingWorkspace.value = workspace?.slug ?? null;
  try {
    const result = await importEntries(slugs);
    if (result.ok) {
      const warnings = CatalogRules.warningLine(result.value);
      notify({
        tone: warnings ? NoticeTone.Warning : NoticeTone.Success,
        title: workspace ? `Imported ${workspace.name}` : 'Catalog imported',
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
      :is-empty="Boolean(catalog.data.value) && !catalog.data.value?.sources.length"
      empty-title="Wheelhouse reads no catalog hubs"
      loading-label="Reading the catalog"
      class="flex flex-col gap-8"
      @retry="refresh.refresh"
    >
      <Text as="p" class="text-sm text-muted-foreground" skeleton-width="14rem"
        ><template v-if="catalog.data.value"
          >{{ entries.length }} apps · {{ importable.length }} importable · {{ hubs?.length ?? 0 }} hubs</template
        ></Text
      >

      <section
        v-for="(hub, index) in hubSlots"
        :key="hub?.source.repository ?? `placeholder-${index}`"
        :aria-label="hub ? `Hub ${hub.name}` : undefined"
        class="flex flex-col gap-4"
      >
        <header class="flex flex-wrap items-baseline gap-x-4 gap-y-1">
          <Heading :level="2" size="md"
            ><Text as="span" skeleton-width="8rem">{{ hub?.name }}</Text></Heading
          >
          <Text as="span" class="min-w-0 truncate font-mono text-xs" skeleton-width="14rem">
            <template v-if="hub">
              <a
                v-if="CatalogRules.sourceHref(hub.source)"
                :href="CatalogRules.sourceHref(hub.source) ?? undefined"
                target="_blank"
                rel="noopener noreferrer"
                class="inline-flex items-center gap-1 hover:text-primary hover:underline"
                >{{ CatalogRules.sourceLabel(hub.source) }}<ExternalLink :size="12"
              /></a>
              <template v-else>{{ CatalogRules.sourceLabel(hub.source) }}</template>
            </template>
          </Text>
          <Text as="span" class="text-sm text-muted-foreground" skeleton-width="8rem">{{
            hub ? CatalogRules.sourceCountsLine(hub) : ''
          }}</Text>
        </header>

        <Alert v-if="hub?.source.error" severity="danger" :title="`${hub.name} is not listed`" :description="hub.source.error" />

        <CatalogWorkspacePanel
          v-for="(group, groupIndex) in hub ? hub.groups : placeholders"
          :key="group?.slug ?? `placeholder-${groupIndex}`"
          :group="group"
          :selected="selection"
          :is-known="group ? known.has(group.slug) : true"
          :is-importing="Boolean(group) && importingWorkspace === group?.slug"
          :is-busy="importing"
          @select="group && selectIn(group, $event)"
          @select-all="group && selectAllIn(group, $event)"
          @import="group && runImport(group.importable, group)"
        />
      </section>
    </DataRegion>
  </div>
</template>
