<script lang="ts">
/** The audit trail's actions, across the portfolio or about one product. */
export interface AuditEntriesPanelProps {
  /** Keeps the entries about one product, by slug; every entry without it. */
  readonly product?: string | null;
}
</script>
<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
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
 * Lists actions newest first, a hundred at a time, with an area filter and a button that loads the older ones after
 * the last page's token. The Activity page shows every entry; a product's Activity tab passes its product and the host
 * keeps the entries about it.
 */
defineOptions({ name: "AuditEntriesPanel" });
const props = withDefaults(defineProps<AuditEntriesPanelProps>(), {
  product: null,
  description: "Every deploy, build, reconcile, secret and product change, newest first.",
});
defineSlots<{}>();

const PageSize = 100;
const AreaLabels: Record<AuditArea, string> = {
  [AuditArea.All]: "All actions",
  [AuditArea.Deployments]: "Deployments and builds",
  [AuditArea.Secrets]: "Secrets",
  [AuditArea.Products]: "Products and environments",
};

const area = ref<AuditArea>(AuditArea.All);
/* Another product reads from its newest entries: the query's key holds it. */
const entries = useAuditEntries(PageSize, () => props.product);
const visible = computed(() => entries.items.value.filter((entry) => AuditExtensions.inArea(entry.action, area.value)));
/** The shown entries, or placeholder rows until the first page arrives. */
const rows = useSkeletonItems(() => (entries.loading.value ? undefined : visible.value), 8);

/** Every action changes the trail, so a visit always reads it fresh. */
onMounted(() => void entries.refetch());
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
      :is-first-load="entries.loading.value"
      :error="entries.error.value"
      :has-data="!entries.loading.value"
      :is-empty="!entries.loading.value && visible.length === 0"
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
        :aria-busy="entries.isFetchingNextPage.value || undefined"
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
    <div v-if="entries.hasNextPage.value" class="mt-4 flex justify-center">
      <Button
        variant="outline"
        tone="neutral"
        size="sm"
        :is-loading="entries.isFetchingNextPage.value"
        class="max-md:min-h-11"
        @click="entries.fetchNextPage()"
        >Load older actions</Button
      >
    </div>
  </Panel>
</template>
