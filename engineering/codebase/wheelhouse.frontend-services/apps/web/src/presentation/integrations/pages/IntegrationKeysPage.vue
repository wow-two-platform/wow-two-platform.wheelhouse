<script setup lang="ts">
import { computed, ref } from "vue";
import { KeyRound, Plus } from "lucide-vue-next";
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
import { Alert, DataRegion, useSkeletonItems } from "@wow-two-beta/fe/presentation/feedback";
import { ConfirmPopover } from "@wow-two-beta/fe/presentation/overlays";
import { PageHeaderActions } from "@wow-two-beta/fe/presentation/layout";
import { useIntegrationKeys } from "@/application/integrations";
import type { IntegrationKey } from "@/domain/integrations";
import { Panel } from "@/presentation/common/components";
import CreateKeyModal from "../components/CreateKeyModal.vue";

/** Lists the keys other programs present, with creation and confirmed revocation. */
defineOptions({ name: "IntegrationKeysPage" });

const { keys, isFirstLoad, hasData, error, reload, create, revoke } = useIntegrationKeys();
const creating = ref(false);
const revokeError = ref<string | null>(null);
const live = computed(() => keys.value.filter((key) => key.revokedAt === null).length);
/** The keys, or placeholder rows until the first list arrives. */
const rows = useSkeletonItems(() => (hasData.value ? keys.value : undefined), 3);

/** Formats an instant for the revoked badge's tooltip. @internal */
function when(value: string): string {
  return new Date(value).toLocaleString();
}

/** Revokes only the explicitly confirmed key; a failure rejects, so the confirmation stays open beside its error. */
async function confirmRevoke(key: IntegrationKey): Promise<void> {
  revokeError.value = null;
  const result = await revoke(key.id);
  if (result.ok) return;
  revokeError.value = result.failure.message;
  throw new Error(result.failure.message);
}
</script>

<template>
  <PageHeaderActions>
    <Button @click="creating = true"><template #leading><Plus :size="16" /></template>Create key</Button>
  </PageHeaderActions>
  <!-- The page has no refresh control, so a failed first load offers its own retry. -->
  <DataRegion
    :is-first-load="isFirstLoad"
    :error="error"
    :has-data="hasData"
    :is-empty="hasData && keys.length === 0"
    empty-title="No integration keys"
    loading-label="Loading integration keys"
    @retry="reload"
  >
    <template #emptyActions
      ><Button @click="creating = true"
        ><template #leading><KeyRound :size="16" /></template>Create key</Button
      ></template
    >
    <Panel :title="`${live} live ${live === 1 ? 'key' : 'keys'}`">
      <Alert v-if="revokeError" severity="danger" :description="revokeError" class="mb-3" />
      <Table density="compact" is-hoverable class="md:min-w-[44rem]">
        <TableHead>
          <TableRow>
            <TableHeaderCell>Name</TableHeaderCell>
            <TableHeaderCell>Key</TableHeaderCell>
            <TableHeaderCell>Scopes</TableHeaderCell>
            <TableHeaderCell>Created</TableHeaderCell>
            <TableHeaderCell>Last used</TableHeaderCell>
            <TableHeaderCell><span class="sr-only">Actions</span></TableHeaderCell>
          </TableRow>
        </TableHead>
        <TableBody>
          <TableRow
            v-for="(key, index) in rows"
            :key="key?.id ?? `placeholder-${index}`"
            :class="key?.revokedAt ? 'text-muted-foreground' : ''"
          >
            <TableCell class="font-medium">{{ key?.name }}</TableCell>
            <TableCell class="font-mono text-xs">{{ key ? `${key.prefix}…` : "" }}</TableCell>
            <TableCell>
              <span class="flex flex-wrap gap-1">
                <template v-if="key">
                  <Badge v-for="scope in key.scopes" :key="scope" size="sm" variant="neutral">{{ scope }}</Badge>
                </template>
                <Badge v-else size="sm" variant="neutral">products:read</Badge>
              </span>
            </TableCell>
            <TableCell class="text-xs"
              ><span v-if="key"><RelativeTime :value="key.createdAt" /> · {{ key.createdBy }}</span></TableCell
            >
            <TableCell class="text-xs"
              ><RelativeTime v-if="key?.lastUsedAt" :value="key.lastUsedAt" /><template v-else-if="key"
                >—</template
              ></TableCell
            >
            <TableCell label="" class="justify-end text-right">
              <template v-if="key">
                <Badge v-if="key.revokedAt" size="sm" variant="outline" :title="`Revoked ${when(key.revokedAt)}`"
                  >Revoked</Badge
                >
                <ConfirmPopover
                  v-else
                  :title="`Revoke ${key.name}?`"
                  confirm-label="Revoke"
                  tone="danger"
                  placement="top-end"
                  :on-confirm="() => confirmRevoke(key)"
                >
                  <Button size="sm" variant="ghost" tone="danger">Revoke</Button>
                </ConfirmPopover>
              </template>
              <Button v-else size="sm" variant="ghost" tone="danger">Revoke</Button>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </Panel>
  </DataRegion>
  <CreateKeyModal :open="creating" :create="create" @update:open="creating = $event" />
</template>
