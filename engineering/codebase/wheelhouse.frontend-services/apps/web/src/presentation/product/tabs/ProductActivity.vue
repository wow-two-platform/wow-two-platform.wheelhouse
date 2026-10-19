<script lang="ts">
/** A product's Activity tab. */
export interface ProductActivityProps {
  /** The product's slug; the host keeps the audit entries about it. */
  readonly product: string;
}
</script>
<script setup lang="ts">
import { PageHeaderActions } from "@wow-two-beta/fe/presentation/layout";
import { AuditKeys } from "@/application/audit";
import { useInvalidate, useRefresh } from "@/bootstrap/query";
import AuditEntriesPanel from "@/presentation/audit/components/AuditEntriesPanel.vue";
import { RefreshButton } from "@/presentation/common/components";

/**
 * The audit trail's entries about the product — its own edits, its services' status, its environments, its
 * deployments and builds, and changes to its vault namespaces — newest first. The whole trail and its verification
 * stay on the Activity page.
 */
defineOptions({ name: "ProductActivity" });
const props = defineProps<ProductActivityProps>();
defineSlots<{}>();
const invalidate = useInvalidate();
const refresh = useRefresh(() => invalidate(AuditKeys.all));
</script>
<template>
  <div>
    <PageHeaderActions>
      <RefreshButton size="md" icon-only :refreshing="refresh.refreshing.value" @refresh="refresh.refresh" />
    </PageHeaderActions>
    <AuditEntriesPanel :product="props.product" />
  </div>
</template>
