<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { ShieldAlert, ShieldCheck } from "lucide-vue-next";
import { Button } from "@wow-two-beta/ui-vue/presentation/actions";
import {
  Badge,
  RelativeTime,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  Text,
} from "@wow-two-beta/ui-vue/presentation/display";
import { DataRegion, useSkeletonItems } from "@wow-two-beta/ui-vue/presentation/feedback";
import {
  SelectPicker,
  SelectPickerContent,
  SelectPickerItem,
  SelectPickerTrigger,
  SelectPickerValue,
} from "@wow-two-beta/ui-vue/presentation/forms";
import { PageHeaderActions } from "@wow-two-beta/ui-vue/presentation/layout";

import { useAuditEntries, useAuditVerification } from "@/application/audit";
import { useRefresh } from "@/bootstrap/query";
import { AuditArea, AuditExtensions, AuditOutcome } from "@/domain/audit";
import { Panel, RefreshButton } from "@/presentation/common/components";

/** Lists every operator action from the hash-chained audit trail, newest first, with the chain's verification. */
defineOptions({ name: "ActivityPage" });

const WindowSize = 100;
const AreaLabels: Record<AuditArea, string> = {
  [AuditArea.All]: "All actions",
  [AuditArea.Deployments]: "Deployments and builds",
  [AuditArea.Secrets]: "Secrets",
  [AuditArea.Products]: "Products",
};

/** The sequence numbers each older window starts below; empty while the newest window shows. */
const windows = ref<number[]>([]);
const before = computed(() => windows.value.at(-1) ?? null);
const area = ref<AuditArea>(AuditArea.All);
const entries = useAuditEntries(WindowSize, before);
const verification = useAuditVerification();
const refresh = useRefresh(() => Promise.all([entries.refetch(), verification.refetch()]));
const visible = computed(() =>
  (entries.data.value ?? []).filter((entry) => AuditExtensions.inArea(entry.action, area.value)),
);
/** The shown entries, or placeholder rows until the first window arrives. */
const rows = useSkeletonItems(() => (entries.data.value ? visible.value : undefined), 8);
const oldest = computed(() => entries.data.value?.at(-1)?.sequence ?? null);
const hasOlder = computed(() => (entries.data.value?.length ?? 0) === WindowSize && (oldest.value ?? 0) > 1);

/** Every operator action changes the trail, so a visit always reads it fresh. */
onMounted(() => void Promise.all([entries.refetch(), verification.refetch()]));

function older(): void {
  if (oldest.value !== null) windows.value = [...windows.value, oldest.value];
}
function newer(): void {
  windows.value = windows.value.slice(0, -1);
}
</script>

<template>
  <div class="space-y-6">
    <PageHeaderActions
      ><RefreshButton size="md" :refreshing="refresh.refreshing.value" @refresh="refresh.refresh"
    /></PageHeaderActions
    >
    <section class="rounded-2xl border border-border bg-card p-5" aria-label="Audit chain">
      <DataRegion
        :is-first-load="verification.isFirstLoad.value"
        :error="verification.error.value"
        :has-data="Boolean(verification.data.value)"
        loading-label="Verifying the audit chain"
      >
        <template #unavailable>
          <Text class="text-sm text-muted-foreground">The chain could not be verified.</Text>
        </template>
        <div class="flex items-start gap-3" role="status">
          <ShieldAlert
            v-if="verification.data.value && !verification.data.value.intact"
            :size="22"
            class="mt-0.5 shrink-0 text-destructive"
            aria-hidden="true"
          />
          <ShieldCheck
            v-else
            :size="22"
            class="mt-0.5 shrink-0"
            :class="verification.data.value ? 'text-success' : 'text-muted-foreground'"
            aria-hidden="true"
          />
          <div v-if="verification.data.value && !verification.data.value.intact" class="min-w-0">
            <Text class="font-medium text-destructive">
              Chain broken at entry {{ verification.data.value.brokenSequence }}:
              {{ AuditExtensions.breakLabel(verification.data.value.reason) }}
            </Text>
            <Text class="mt-1 text-sm text-muted-foreground">
              Someone with database access changed the trail. Entries from that point on are not proven.
            </Text>
          </div>
          <div v-else class="min-w-0">
            <Text class="font-medium">
              Chain intact · {{ verification.data.value?.entries ?? 0 }}
              {{ verification.data.value?.entries === 1 ? "entry" : "entries" }}
            </Text>
            <Text class="mt-1 text-sm text-muted-foreground">
              No stored entry was edited, reordered or removed from the middle. A verified chain cannot show that
              the newest entries were kept.
            </Text>
          </div>
        </div>
      </DataRegion>
    </section>

    <Panel title="Operator actions" description="Every deploy, build, reconcile, secret and product change, newest first.">
      <template #actions>
        <SelectPicker v-model="area" :get-option-label="(value) => AreaLabels[value as AuditArea]"
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
          area === AuditArea.All
            ? 'Deploys, builds, reconciles and secret or product changes appear here.'
            : 'No action in this area within the entries shown.'
        "
        loading-label="Loading operator actions"
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
              <TableHeaderCell>When</TableHeaderCell><TableHeaderCell>Operator</TableHeaderCell
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
  </div>
</template>
