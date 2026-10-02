<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { ShieldAlert, ShieldCheck } from "lucide-vue-next";
import { Button } from "@wow-two-beta/ui-vue/presentation/actions";
import {
  Badge,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
} from "@wow-two-beta/ui-vue/presentation/display";
import {
  SelectPicker,
  SelectPickerContent,
  SelectPickerItem,
  SelectPickerTrigger,
  SelectPickerValue,
} from "@wow-two-beta/ui-vue/presentation/forms";

import { useAuditEntries, useAuditVerification } from "@/application/audit";
import { useRefresh } from "@/bootstrap/query";
import { AuditArea, AuditExtensions, AuditOutcome } from "@/domain/audit";
import { Measures } from "@/domain/common";
import { LoadState, PageActions, Panel, RefreshButton } from "@/presentation/common/components";

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
    <PageActions
      ><RefreshButton size="md" :refreshing="refresh.refreshing.value" @refresh="refresh.refresh"
    /></PageActions
    >
    <section
      class="flex flex-wrap items-start gap-3 rounded-2xl border border-border bg-card p-5"
      aria-label="Audit chain"
      role="status"
    >
      <template v-if="verification.data.value?.intact">
        <ShieldCheck :size="22" class="mt-0.5 shrink-0 text-success" aria-hidden="true" />
        <div>
          <p class="font-medium">
            Chain intact · {{ verification.data.value.entries }}
            {{ verification.data.value.entries === 1 ? "entry" : "entries" }}
          </p>
          <p class="mt-1 text-sm text-muted-foreground">
            No stored entry was edited, reordered or removed from the middle. A verified chain cannot show that
            the newest entries were kept.
          </p>
        </div>
      </template>
      <template v-else-if="verification.data.value">
        <ShieldAlert :size="22" class="mt-0.5 shrink-0 text-destructive" aria-hidden="true" />
        <div>
          <p class="font-medium text-destructive">
            Chain broken at entry {{ verification.data.value.brokenSequence }}:
            {{ AuditExtensions.breakLabel(verification.data.value.reason) }}
          </p>
          <p class="mt-1 text-sm text-muted-foreground">
            Someone with database access changed the trail. Entries from that point on are not proven.
          </p>
        </div>
      </template>
      <p v-else-if="verification.error.value" class="text-sm text-muted-foreground">
        The chain could not be verified: {{ verification.error.value.message }}
      </p>
      <p v-else class="text-sm text-muted-foreground">Verifying the audit chain…</p>
    </section>

    <Panel title="Operator actions" description="Every deploy, build, reconcile, secret and product change, newest first.">
      <template #actions>
        <div class="w-56">
          <SelectPicker v-model="area"
            ><SelectPickerTrigger aria-label="Area"><SelectPickerValue /></SelectPickerTrigger
            ><SelectPickerContent
              ><SelectPickerItem
                v-for="(label, value) in AreaLabels"
                :key="value"
                :item-key="value"
                :value="value"
                :label="label" /></SelectPickerContent
          ></SelectPicker>
        </div>
      </template>
      <LoadState
        :loading="entries.loading.value && !entries.data.value"
        :error="entries.error.value"
        :has-data="Boolean(entries.data.value)"
        :empty="visible.length === 0"
        empty-title="No actions recorded"
        :empty-description="
          area === AuditArea.All
            ? 'Deploys, builds, reconciles and secret or product changes appear here.'
            : 'No action in this area within the entries shown.'
        "
        @retry="entries.refetch()"
      >
        <Table density="compact" is-hoverable container-class-name="overflow-auto">
          <TableHead class="sticky top-0 z-10 bg-card">
            <TableRow>
              <TableHeaderCell>When</TableHeaderCell><TableHeaderCell>Operator</TableHeaderCell
              ><TableHeaderCell>Action</TableHeaderCell><TableHeaderCell>Subject</TableHeaderCell
              ><TableHeaderCell>Outcome</TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            <TableRow v-for="entry in visible" :key="entry.sequence">
              <TableCell
                class="whitespace-nowrap text-xs text-muted-foreground"
                :title="`#${entry.sequence} · ${new Date(entry.occurredAt).toLocaleString()}`"
                >{{ Measures.moment(entry.occurredAt) }}</TableCell
              >
              <TableCell class="text-xs">{{ entry.actor }}</TableCell>
              <TableCell>
                <span class="block text-sm font-medium">{{ AuditExtensions.label(entry.action) }}</span>
                <span v-if="entry.detail" class="block text-xs text-muted-foreground">{{ entry.detail }}</span>
              </TableCell>
              <TableCell class="font-mono text-xs break-all">{{ entry.subject }}</TableCell>
              <TableCell>
                <Badge :variant="entry.outcome === AuditOutcome.Succeeded ? 'success' : 'danger'">{{
                  entry.outcome === AuditOutcome.Succeeded ? "Done" : "Failed"
                }}</Badge>
                <span v-if="entry.reason" class="mt-1 block max-w-xs text-xs text-muted-foreground">{{
                  entry.reason
                }}</span>
              </TableCell>
            </TableRow>
          </TableBody>
        </Table>
      </LoadState>
      <div v-if="windows.length || hasOlder" class="mt-4 flex justify-between gap-3">
        <Button variant="ghost" size="sm" :is-disabled="!windows.length" @click="newer">Newer</Button>
        <Button variant="ghost" size="sm" :is-disabled="!hasOlder" @click="older">Older</Button>
      </div>
    </Panel>
  </div>
</template>
