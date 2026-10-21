<script lang="ts">
import type { CatalogWorkspaceGroup } from "@/domain/catalog";

/** Defines one catalog workspace's panel. */
export interface CatalogWorkspacePanelProps {
  /** The workspace and its apps; undefined draws a placeholder while the catalog is first read. */
  readonly group: CatalogWorkspaceGroup | undefined;
  /** The whole page's selection; this panel reads and changes its own apps' part. */
  readonly selected: readonly string[];
  /** Whether Wheelhouse has this workspace already; an import creates the one it lacks. */
  readonly isKnown: boolean;
  /** This workspace's import runs now. */
  readonly isImporting: boolean;
  /** Any import runs now, so no other starts. */
  readonly isBusy: boolean;
}
</script>

<script setup lang="ts">
import { computed } from "vue";
import { Download } from "lucide-vue-next";

import { Button } from "@wow-two-beta/fe/presentation/actions";
import { Badge, DataTable, type DataTableColumn } from "@wow-two-beta/fe/presentation/display";
import { CheckboxField } from "@wow-two-beta/fe/presentation/forms";

import { CatalogEntryState, CatalogRules, WorkspaceSelection, type CatalogEntry } from "@/domain/catalog";
import { Panel } from "@/presentation/common/components";

/** @internal Each entry state's badge. */
const States = {
  [CatalogEntryState.New]: { label: "New", variant: "info" },
  [CatalogEntryState.InWheelhouse]: { label: "In Wheelhouse", variant: "success" },
  [CatalogEntryState.External]: { label: "Not deployable", variant: "neutral" },
} as const;

/** @internal The table's columns; the `cell` slot renders each. */
const Columns: DataTableColumn<CatalogEntry>[] = [
  { key: "app", header: "App", accessor: (entry) => entry.name, isSortable: true },
  { key: "kind", header: "Kind", accessor: (entry) => entry.kind, isSortable: true, width: "12rem" },
  { key: "state", header: "State", accessor: (entry) => CatalogRules.stateOf(entry), isSortable: true, width: "12rem" },
];

/**
 * One catalog workspace: its name, whether Wheelhouse has it yet, and how many of its apps can be imported, are in
 * Wheelhouse already or deploy elsewhere; then its apps, in columns as wide in every workspace. Where any can be
 * imported, "Select all importable" adds or clears them all in the page's selection and "Import workspace" imports them
 * at once.
 */
defineOptions({ name: "CatalogWorkspacePanel" });
const props = defineProps<CatalogWorkspacePanelProps>();
const emit = defineEmits<{ select: [slugs: readonly unknown[]]; selectAll: [isSelected: boolean]; import: [] }>();

const importable = computed(() => props.group?.importable ?? []);
const held = computed(() => importable.value.filter((slug) => props.selected.includes(slug)));
const state = computed(() =>
  props.group ? CatalogRules.workspaceSelection(props.group, props.selected) : WorkspaceSelection.None,
);
</script>

<template>
  <Panel
    :title="props.group?.name ?? ''"
    v-bind="props.group ? { description: CatalogRules.countsLine(props.group) } : {}"
  >
    <template #title>
      <span class="inline-flex min-w-0 flex-wrap items-baseline gap-x-2 gap-y-1">
        <span>{{ props.group?.name }}</span>
        <span v-if="props.group" class="font-mono text-xs font-normal text-muted-foreground">{{
          props.group.slug
        }}</span>
        <Badge v-if="props.group && !props.isKnown" size="sm" variant="info" title="Importing creates this workspace"
          >New</Badge
        >
      </span>
    </template>
    <template v-if="importable.length" #actions>
      <!-- Side by side from md; stacked on a phone, where the pair is wider than the panel. -->
      <div class="flex flex-col items-start gap-1 md:flex-row md:items-center md:gap-3">
        <CheckboxField
          label="Select all importable"
          wrapper-class-name="text-sm max-md:min-h-11"
          :model-value="state === WorkspaceSelection.All"
          :is-indeterminate="state === WorkspaceSelection.Some"
          :is-disabled="props.isBusy"
          @update:model-value="emit('selectAll', state !== WorkspaceSelection.All)"
        />
        <Button
          size="sm"
          variant="outline"
          tone="neutral"
          class="max-md:min-h-11"
          :is-disabled="props.isBusy && !props.isImporting"
          :is-loading="props.isImporting"
          :aria-label="`Import workspace ${props.group?.name ?? ''}`"
          @click="emit('import')"
        >
          <template #leading><Download :size="14" aria-hidden="true" /></template>Import workspace
        </Button>
      </div>
    </template>
    <DataTable
      :columns="Columns"
      :data="props.group?.entries ?? []"
      :row-key="(entry) => entry.slug"
      selection-mode="multiple"
      :selection="held"
      :is-row-selectable="(entry) => CatalogRules.isImportable(entry)"
      :skeleton-rows="3"
      density="compact"
      empty-content="This workspace lists no apps."
      @update:selection="emit('select', $event)"
    >
      <template #cell="{ row, column }">
        <template v-if="column.key === 'app'">
          <span class="block font-medium">{{ row.name }}</span>
          <span class="block font-mono text-xs text-muted-foreground">{{ row.slug }}</span>
          <!-- One product of several repositories (decision 70), and the products importing it retires. -->
          <span
            v-if="row.repositories.length > 1"
            class="block text-xs text-muted-foreground"
            :title="row.repositories.join(', ')"
            >{{ row.repositories.length }} repositories</span
          >
          <span
            v-if="row.replaces.length"
            class="block text-xs text-muted-foreground"
            :title="'Importing retires these when nothing was deployed to them'"
            >Replaces {{ row.replaces.join(", ") }}</span
          >
        </template>
        <span v-else-if="column.key === 'kind'" class="capitalize">{{ row.kind }}</span>
        <Badge
          v-else-if="column.key === 'state'"
          size="sm"
          :variant="States[CatalogRules.stateOf(row)].variant"
          :title="
            CatalogRules.stateOf(row) !== CatalogEntryState.External
              ? undefined
              : row.deploy === 'none'
                ? 'Not deployed'
                : `Deployed by ${row.deploy}`
          "
          >{{ States[CatalogRules.stateOf(row)].label }}</Badge
        >
      </template>
    </DataTable>
  </Panel>
</template>
