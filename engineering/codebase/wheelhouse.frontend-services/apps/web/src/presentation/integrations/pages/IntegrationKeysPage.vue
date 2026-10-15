<script setup lang="ts">
import { computed, ref } from "vue";
import { KeyRound, Plus } from "lucide-vue-next";
import { Button } from "@wow-two-beta/ui-vue/presentation/actions";
import { Badge } from "@wow-two-beta/ui-vue/presentation/display";
import { Alert } from "@wow-two-beta/ui-vue/presentation/feedback";
import { useIntegrationKeys } from "@/application/integrations";
import type { IntegrationKey } from "@/domain/integrations";
import { LoadState, PageActions, Panel } from "@/presentation/common/components";
import CreateKeyModal from "../components/CreateKeyModal.vue";

/** Lists the keys other programs present, with creation and confirmed revocation. */
defineOptions({ name: "IntegrationKeysPage" });

const { keys, loading, error, reload, create, revoke } = useIntegrationKeys();
const creating = ref(false);
const confirming = ref<string | null>(null);
const revoking = ref(false);
const revokeError = ref<string | null>(null);
const live = computed(() => keys.value.filter((key) => key.revokedAt === null).length);

/** Formats an instant for the table, or a dash when absent. @internal */
function when(value: string | null): string {
  return value ? new Date(value).toLocaleString() : "—";
}

/** Revokes only the explicitly confirmed key. */
async function confirmRevoke(key: IntegrationKey): Promise<void> {
  if (revoking.value) return;
  revoking.value = true;
  revokeError.value = null;
  try {
    const result = await revoke(key.id);
    if (result.ok) confirming.value = null;
    else revokeError.value = result.failure.message;
  } finally {
    revoking.value = false;
  }
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
      <div class="overflow-x-auto">
        <table class="w-full min-w-[44rem] text-left text-sm">
          <thead class="text-xs text-muted-foreground">
            <tr>
              <th class="py-2 pr-4 font-medium">Name</th>
              <th class="py-2 pr-4 font-medium">Key</th>
              <th class="py-2 pr-4 font-medium">Scopes</th>
              <th class="py-2 pr-4 font-medium">Created</th>
              <th class="py-2 pr-4 font-medium">Last used</th>
              <th class="py-2 font-medium"><span class="sr-only">Actions</span></th>
            </tr>
          </thead>
          <tbody class="divide-y divide-border">
            <tr v-for="key in keys" :key="key.id" :class="key.revokedAt ? 'text-muted-foreground' : ''">
              <td class="py-3 pr-4 font-medium">{{ key.name }}</td>
              <td class="py-3 pr-4 font-mono text-xs">{{ key.prefix }}…</td>
              <td class="py-3 pr-4">
                <span class="flex flex-wrap gap-1">
                  <Badge v-for="scope in key.scopes" :key="scope" size="sm" variant="neutral">{{ scope }}</Badge>
                </span>
              </td>
              <td class="py-3 pr-4 text-xs">{{ when(key.createdAt) }} · {{ key.createdBy }}</td>
              <td class="py-3 pr-4 text-xs">{{ when(key.lastUsedAt) }}</td>
              <td class="py-3 text-right">
                <Badge v-if="key.revokedAt" size="sm" variant="outline" :title="`Revoked ${when(key.revokedAt)}`">Revoked</Badge>
                <span v-else-if="confirming === key.id" class="inline-flex gap-2">
                  <Button size="sm" variant="ghost" tone="neutral" :is-disabled="revoking" @click="confirming = null">Cancel</Button>
                  <Button size="sm" tone="danger" :is-loading="revoking" @click="confirmRevoke(key)">Revoke</Button>
                </span>
                <Button v-else size="sm" variant="ghost" tone="danger" @click="confirming = key.id">Revoke</Button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </Panel>
  </LoadState>
  <CreateKeyModal :open="creating" :create="create" @update:open="creating = $event" />
</template>
