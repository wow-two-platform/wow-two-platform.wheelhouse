<script setup lang="ts">
import { onMounted } from "vue";
import { ShieldAlert, ShieldCheck } from "lucide-vue-next";
import { Text } from "@wow-two-beta/fe/presentation/display";
import { DataRegion } from "@wow-two-beta/fe/presentation/feedback";
import { PageHeaderActions } from "@wow-two-beta/fe/presentation/layout";

import { AuditKeys, useAuditVerification } from "@/application/audit";
import { useInvalidate, useRefresh } from "@/bootstrap/query";
import { AuditExtensions } from "@/domain/audit";
import { RefreshButton } from "@/presentation/common/components";
import AuditEntriesPanel from "../components/AuditEntriesPanel.vue";

/**
 * Every action from the hash-chained audit trail, newest first, with the chain's verification — the
 * portfolio-wide view the profile menu opens. Each product's own entries are its page's Activity tab.
 */
defineOptions({ name: "ActivityPage" });

const verification = useAuditVerification();
const invalidate = useInvalidate();
const refresh = useRefresh(() => invalidate(AuditKeys.all));

/** Every action changes the trail, so a visit always reads it fresh. */
onMounted(() => void verification.refetch());
</script>

<template>
  <div class="space-y-6">
    <PageHeaderActions
      ><RefreshButton size="md" :refreshing="refresh.refreshing.value" @refresh="refresh.refresh"
    /></PageHeaderActions>
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
    <AuditEntriesPanel />
  </div>
</template>
