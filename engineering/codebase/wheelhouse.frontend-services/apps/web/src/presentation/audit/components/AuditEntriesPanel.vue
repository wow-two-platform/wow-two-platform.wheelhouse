<script lang="ts">
/** The audit trail's actions, across the portfolio or about one product. */
export interface AuditEntriesPanelProps {
  /** Keeps the entries about one product, by slug; every entry without it. */
  readonly product?: string | null;
}
</script>
<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import { Button } from "@wow-two-beta/fe/presentation/actions";
import {
  Badge,
  RelativeTime,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
} from "@wow-two-beta/fe/presentation/display";
import { DataRegion, useSkeletonItems } from "@wow-two-beta/fe/presentation/feedback";
import {
  SelectPicker,
  SelectPickerContent,
  SelectPickerItem,
  SelectPickerTrigger,
  SelectPickerValue,
} from "@wow-two-beta/fe/presentation/forms";
import { useAuditEntries } from "@/application/audit";
import { AuditArea, AuditExtensions, AuditOutcome } from "@/domain/audit";
import { InfoPopover, Panel } from "@/presentation/common/components";

/**
 * Lists actions newest first, a hundred at a time, with an area filter and paging back. The Activity page
 * shows every entry; a product's Activity tab passes its product and the host keeps the entries about it.
 */
defineOptions({ name: "AuditEntriesPanel" });
const props = withDefaults(defineProps<AuditEntriesPanelProps>(), {
  product: null,
  description: "Every deploy, build, reconcile, secret and product change, newest first.",
});
defineSlots<{}>();

const WindowSize = 100;
const AreaLabels: Record<AuditArea, string> = {
  [AuditArea.All]: "All actions",
  [AuditArea.Deployments]: "Deployments and builds",
  [AuditArea.Secrets]: "Secrets",
  [AuditArea.Products]: "Products and environments",
};

/** The sequence numbers each older window starts below; empty while the newest window shows. */
const windows = ref<number[]>([]);
const before = computed(() => windows.value.at(-1) ?? null);
const area = ref<AuditArea>(AuditArea.All);
const entries = useAuditEntries(WindowSize, before, () => props.product);
const visible = computed(() =>
  (entries.data.value ?? []).filter((entry) => AuditExtensions.inArea(entry.action, area.value)),
);
/** The shown entries, or placeholder rows until the first window arrives. */
const rows = useSkeletonItems(() => (entries.data.value ? visible.value : undefined), 8);
const oldest = computed(() => entries.data.value?.at(-1)?.sequence ?? null);
const hasOlder = computed(() => (entries.data.value?.length ?? 0) === WindowSize && (oldest.value ?? 0) > 1);

/** Every action changes the trail, so a visit always reads it fresh. */
onMounted(() => void entries.refetch());
/* Another product starts from its newest entries. */
watch(
  () => props.product,
  () => (windows.value = []),
);

function older(): void {
  if (oldest.value !== null) windows.value = [...windows.value, oldest.value];
}
function newer(): void {
  windows.value = windows.value.slice(0, -1);
}
</script>

<template>
  <Panel title="Actions">
    <template #info>
      <InfoPopover title="Actions">
        <p>
          Every audited action{{ props.product ? " about this product" : "" }} — edits, environments, deployments,
          builds and secret changes — newest first, with who took it: a signed-in owner, or an integration key. The
          trail is hash-chained, so an entry cannot change unseen, and it never holds a secret's value.
        </p>
      </InfoPopover>
    </template>
    <template #actions>
      <SelectPicker v-model="area"
        ><SelectPickerTrigger aria-label="Area" width="14rem"><SelectPickerValue /></SelectPickerTrigger
        ><SelectPickerContent
          ><SelectPickerItem
            v-for="(label, value) in AreaLabels"
            :key="value"
            :item-key="value"
            :value="value"
            :label="label" /></SelectPickerContent
      ></SelectPicker>
    </template>
    <DataRegion
      :is-first-load="entries.isFirstLoad.value"
      :is-refreshing="entries.isRefreshing.value"
      :error="entries.error.value"
      :has-data="Boolean(entries.data.value)"
      :is-empty="Boolean(entries.data.value) && visible.length === 0"
      empty-title="No actions recorded"
      :empty-description="
        area !== AuditArea.All
          ? 'No action in this area within the entries shown.'
          : props.product
            ? 'Deploys, builds, secret changes and edits to this product appear here.'
            : 'Deploys, builds, reconciles and secret or product changes appear here.'
      "
      loading-label="Loading actions"
    >
      <Table
        density="compact"
        is-hoverable
        has-sticky-header
        container-class-name="overflow-auto"
        :class="entries.isPreviousData.value ? 'opacity-60' : undefined"
        :aria-busy="entries.isRefreshing.value || undefined"
      >
        <TableHead>
          <TableRow>
            <TableHeaderCell>When</TableHeaderCell><TableHeaderCell>Actor</TableHeaderCell
            ><TableHeaderCell>Action</TableHeaderCell><TableHeaderCell>Subject</TableHeaderCell
            ><TableHeaderCell>Outcome</TableHeaderCell>
          </TableRow>
        </TableHead>
        <TableBody>
          <TableRow v-for="(entry, index) in rows" :key="entry?.sequence ?? `placeholder-${index}`">
            <!-- A placeholder row leaves composite cells empty, so each draws one bar. -->
            <TableCell class="whitespace-nowrap text-xs text-muted-foreground">
              <span v-if="entry" class="block">
                <RelativeTime class="block" :value="entry.occurredAt" />
                <span class="mt-1 block font-mono">#{{ entry.sequence }}</span>
              </span>
            </TableCell>
            <TableCell class="text-xs">{{ entry?.actor }}</TableCell>
            <TableCell>
              <span v-if="entry" class="block min-w-0">
                <span class="block text-sm font-medium">{{ AuditExtensions.label(entry.action) }}</span>
                <span v-if="entry.detail" class="block text-xs text-muted-foreground">{{ entry.detail }}</span>
              </span>
            </TableCell>
            <TableCell class="font-mono text-xs break-all">{{ entry?.subject }}</TableCell>
            <TableCell>
              <span class="block min-w-0">
                <Badge :variant="entry?.outcome === AuditOutcome.Failed ? 'danger' : 'success'">{{
                  entry?.outcome === AuditOutcome.Failed ? "Failed" : "Done"
                }}</Badge>
                <span v-if="entry?.reason" class="mt-1 block max-w-xs text-xs text-muted-foreground">{{
                  entry.reason
                }}</span>
              </span>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </DataRegion>
    <div v-if="windows.length || hasOlder" class="mt-4 flex justify-between gap-3">
      <Button variant="ghost" size="sm" :is-disabled="!windows.length || entries.isPreviousData.value" @click="newer"
        >Newer</Button
      >
      <Button variant="ghost" size="sm" :is-disabled="!hasOlder || entries.isPreviousData.value" @click="older"
        >Older</Button
      >
    </div>
  </Panel>
</template>
