<script setup lang="ts">
import { computed, ref } from "vue";
import { KeyRound, Plus } from "lucide-vue-next";
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
} from "@wow-two-beta/ui-vue/presentation/display";
import { Alert } from "@wow-two-beta/ui-vue/presentation/feedback";
import { ConfirmPopover } from "@wow-two-beta/ui-vue/presentation/overlays";
import { useIntegrationKeys } from "@/application/integrations";
import type { IntegrationKey } from "@/domain/integrations";
import { LoadState, PageActions, Panel } from "@/presentation/common/components";
import CreateKeyModal from "../components/CreateKeyModal.vue";

/** Lists the keys other programs present, with creation and confirmed revocation. */
defineOptions({ name: "IntegrationKeysPage" });

const { keys, loading, error, reload, create, revoke } = useIntegrationKeys();
const creating = ref(false);
const revokeError = ref<string | null>(null);
const live = computed(() => keys.value.filter((key) => key.revokedAt === null).length);

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
  <PageActions>
    <Button @click="creating = true"><template #leading><Plus :size="16" /></template>Create key</Button>
  </PageActions>
  <LoadState
    :loading="loading && keys.length === 0"
    :error="error"
    :has-data="keys.length > 0"
    :empty="keys.length === 0"
    empty-title="No integration keys"
    @retry="reload"
  >
    <template #emptyActions
      ><Button @click="creating = true"
        ><template #leading><KeyRound :size="16" /></template>Create key</Button
      ></template
    >
    <Panel :title="`${live} live ${live === 1 ? 'key' : 'keys'}`">
      <Alert v-if="revokeError" severity="danger" :description="revokeError" class="mb-3" />
      <Table density="compact" is-hoverable class="min-w-[44rem]">
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
          <TableRow v-for="key in keys" :key="key.id" :class="key.revokedAt ? 'text-muted-foreground' : ''">
            <TableCell class="font-medium">{{ key.name }}</TableCell>
            <TableCell class="font-mono text-xs">{{ key.prefix }}…</TableCell>
            <TableCell>
              <span class="flex flex-wrap gap-1">
                <Badge v-for="scope in key.scopes" :key="scope" size="sm" variant="neutral">{{ scope }}</Badge>
              </span>
            </TableCell>
            <TableCell class="text-xs"><RelativeTime :value="key.createdAt" /> · {{ key.createdBy }}</TableCell>
            <TableCell class="text-xs"
              ><RelativeTime v-if="key.lastUsedAt" :value="key.lastUsedAt" /><template v-else>—</template></TableCell
            >
            <TableCell class="text-right">
              <Badge v-if="key.revokedAt" size="sm" variant="outline" :title="`Revoked ${when(key.revokedAt)}`">Revoked</Badge>
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
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </Panel>
  </LoadState>
  <CreateKeyModal :open="creating" :create="create" @update:open="creating = $event" />
</template>
