<script lang="ts">
import type { OverdueToken, VaultToken } from "@/domain/secrets";
/** Token metadata and hygiene flags for one namespace. */
export interface TokensTableProps {
  vault: string;
  ns: string;
  flags: ReadonlyMap<string, OverdueToken["reason"]>;
}
</script>

<script setup lang="ts">
import { computed, ref, shallowRef, watch } from "vue";
import { Plus } from "lucide-vue-next";
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
import {
  DataRegion,
  SkeletonStateSlot,
  StatusIndicator,
  useSkeletonItems,
} from "@wow-two-beta/fe/presentation/feedback";
import { ConfirmPopover } from "@wow-two-beta/fe/presentation/overlays";
import { useTokenChanges, useVaultTokens } from "@/application/secrets";
import MintTokenModal from "./MintTokenModal.vue";

/** Administers metadata, one-time minting, and token revocation confirmed in the SDK popover. */
defineOptions({ name: "TokensTable" });
const props = defineProps<TokensTableProps>();
defineSlots<{}>();

const { data, error, isRefreshing } = useVaultTokens(
  () => props.vault,
  () => props.ns,
);
const changes = useTokenChanges(
  () => props.vault,
  () => props.ns,
);
const changeError = changes.error;
const changing = changes.loading;
const minting = ref(false);
/** True until the first read, including while no namespace is selected yet: the table draws placeholder rows. */
const pending = computed(() => data.value === undefined && !error.value);
/** The tokens, or placeholder rows until the namespace's first read arrives. */
const rows = useSkeletonItems(() => data.value, 2);
const revoking = shallowRef<VaultToken | null>(null);

/** Closes namespace-bound dialogs before displaying a different context. */
watch([() => props.vault, () => props.ns], () => {
  minting.value = false;
  closeRevoke(false);
});

/** Cancels local confirmation and prevents a late request from changing its replacement. */
function closeRevoke(open: boolean): void {
  if (!open) {
    revoking.value = null;
    changes.reset();
  }
}

/** Retains confirmation and its error until the server confirms revocation. */
async function revoke(): Promise<void> {
  const token = revoking.value;
  if (!token) return;
  const result = await changes.revoke(token.id);
  if (result.ok && revoking.value?.id === token.id) closeRevoke(false);
}

/** Opens one token's confirmation; closing it cancels any pending request. @internal */
function confirmFor(token: VaultToken, open: boolean): void {
  if (open) {
    changes.reset();
    revoking.value = token;
  } else closeRevoke(false);
}

/** The popover's confirm: a failed revocation rejects, so the confirmation stays open with its error. @internal */
async function confirmRevoke(): Promise<void> {
  await revoke();
  if (revoking.value !== null) throw new Error(changeError.value?.message ?? "Revocation did not complete");
}
</script>

<template>
  <div class="flex flex-col gap-4">
    <div class="flex flex-wrap items-center justify-between gap-3">
      <p class="text-sm text-muted-foreground">
        Tokens read secrets from this namespace only.
      </p>
      <Button size="sm" :is-disabled="!props.ns" @click="minting = true">
        <template #leading><Plus :size="14" /></template>Mint token
      </Button>
    </div>
    <DataRegion
      :is-first-load="pending"
      :is-refreshing="isRefreshing"
      :error="error"
      :has-data="data !== undefined"
      :is-empty="data !== undefined && !data.length"
      empty-title="No product tokens"
      empty-description="Mint one per product that reads this namespace."
      loading-label="Loading tokens"
    >
      <Table
        density="compact"
        is-hoverable
        container-class-name="overflow-x-auto rounded-xl border border-border"
      >
        <TableHead
          ><TableRow>
            <TableHeaderCell>Name</TableHeaderCell
            ><TableHeaderCell>Created</TableHeaderCell>
            <TableHeaderCell>Expires</TableHeaderCell
            ><TableHeaderCell>State</TableHeaderCell>
            <TableHeaderCell
              ><span class="sr-only">Actions</span></TableHeaderCell
            >
          </TableRow></TableHead
        >
        <TableBody>
          <TableRow v-for="(token, index) in rows" :key="token?.id ?? `placeholder-${index}`">
            <TableCell>{{ token?.name }}</TableCell>
            <TableCell class="whitespace-nowrap text-xs text-muted-foreground">
              <SkeletonStateSlot
                ><RelativeTime v-if="token" :value="token.createdAtUtc" /><template v-else
                  >3 days ago</template
                ></SkeletonStateSlot
              >
            </TableCell>
            <TableCell class="whitespace-nowrap text-xs text-muted-foreground">
              <SkeletonStateSlot
                ><RelativeTime v-if="token?.expiresAtUtc" :value="token.expiresAtUtc" /><template
                  v-else
                  >Never</template
                ></SkeletonStateSlot
              >
            </TableCell>
            <TableCell>
              <span class="flex flex-wrap items-center gap-2">
                <SkeletonStateSlot
                  ><StatusIndicator
                    :tone="token?.isRevoked ? 'neutral' : 'success'"
                    :label="token?.isRevoked ? 'Revoked' : 'Active'"
                /></SkeletonStateSlot>
                <Badge
                  v-if="token && flags.get(token.id)"
                  :variant="
                    flags.get(token.id) === 'expired' ? 'danger' : 'warning'
                  "
                >
                  {{ flags.get(token.id) }}
                </Badge>
              </span>
            </TableCell>
            <TableCell label="" class="justify-end text-right">
              <ConfirmPopover
                v-if="token && !token.isRevoked"
                :open="revoking?.id === token.id"
                :title="`Revoke ${token.name}?`"
                confirm-label="Revoke"
                tone="danger"
                placement="top-end"
                :on-confirm="confirmRevoke"
                @update:open="confirmFor(token, $event)"
              >
                <template #description
                  >Later reads with this token fail. Products retain loaded
                  values until they restart.<span
                    v-if="changeError && !changing"
                    role="alert"
                    class="mt-2 block text-destructive"
                    >{{ changeError.message }}</span
                  ></template
                >
                <Button variant="ghost" tone="danger" size="sm">Revoke</Button>
              </ConfirmPopover>
              <Button v-else-if="!token" variant="ghost" tone="danger" size="sm">Revoke</Button>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </DataRegion>
    <MintTokenModal :vault="vault" :ns="ns" v-model:open="minting" />
  </div>
</template>
