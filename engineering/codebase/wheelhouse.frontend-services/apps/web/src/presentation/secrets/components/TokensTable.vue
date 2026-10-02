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
  Alert,
  StatusIndicator,
} from "@wow-two-beta/ui-vue/presentation/feedback";
import {
  AlertModal,
  AlertModalContent,
  ModalHeader,
  ModalTitle,
  ModalDescription,
  ModalBody,
  ModalFooter,
} from "@wow-two-beta/ui-vue/presentation/overlays";
import { useTokenChanges, useVaultTokens } from "@/application/secrets";
import { Measures } from "@/domain/common";
import { SkeletonStateSlot } from "@wow-two-beta/ui-vue/presentation/feedback";
import { LoadState } from "@/presentation/common/components";
import MintTokenModal from "./MintTokenModal.vue";

/** Administers metadata, one-time minting, and confirmed token revocation. */
defineOptions({ name: "TokensTable" });
const props = defineProps<TokensTableProps>();
defineSlots<{}>();

const { data, error, refetch } = useVaultTokens(
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
/** Rows shaped like real tokens, shown until the namespace's first read arrives. @internal */
const PlaceholderTokens = ["product-api", "product-worker"];
/** True until the first read: the table renders its header and placeholder rows, never gray blocks. */
const pending = computed(() => data.value === undefined && !error.value);
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
    <LoadState
      :loading="false"
      :error="error"
      :has-data="data !== undefined"
      :empty="data !== undefined && !data.length"
      empty-title="No product tokens"
      empty-description="Mint one per product that reads this namespace."
      @retry="refetch"
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
          <template v-if="pending">
            <TableRow v-for="name in PlaceholderTokens" :key="name" aria-hidden="true">
              <TableCell><SkeletonStateSlot :is-loading="true">{{ name }}</SkeletonStateSlot></TableCell>
              <TableCell class="text-xs"
                ><SkeletonStateSlot :is-loading="true">3 days ago</SkeletonStateSlot></TableCell
              >
              <TableCell class="text-xs"
                ><SkeletonStateSlot :is-loading="true">Never</SkeletonStateSlot></TableCell
              >
              <TableCell><SkeletonStateSlot :is-loading="true">Active</SkeletonStateSlot></TableCell>
              <TableCell />
            </TableRow>
          </template>
          <TableRow v-for="token in data ?? []" :key="token.id">
            <TableCell>{{ token.name }}</TableCell>
            <TableCell class="whitespace-nowrap text-xs text-muted-foreground">
              <SkeletonStateSlot
                :title="new Date(token.createdAtUtc).toLocaleString()"
                >{{ Measures.age(token.createdAtUtc) }}</SkeletonStateSlot
              >
            </TableCell>
            <TableCell class="text-xs text-muted-foreground">
              <SkeletonStateSlot>{{
                token.expiresAtUtc
                  ? new Date(token.expiresAtUtc).toLocaleString()
                  : "Never"
              }}</SkeletonStateSlot>
            </TableCell>
            <TableCell>
              <span class="flex flex-wrap items-center gap-2">
                <SkeletonStateSlot
                  ><StatusIndicator
                    :tone="token.isRevoked ? 'neutral' : 'success'"
                    :label="token.isRevoked ? 'Revoked' : 'Active'"
                /></SkeletonStateSlot>
                <Badge
                  v-if="flags.get(token.id)"
                  :variant="
                    flags.get(token.id) === 'expired' ? 'danger' : 'warning'
                  "
                >
                  {{ flags.get(token.id) }}
                </Badge>
              </span>
            </TableCell>
            <TableCell class="text-right">
              <Button
                v-if="!token.isRevoked"
                variant="ghost"
                tone="danger"
                size="sm"
                @click="revoking = token"
              >
                Revoke
              </Button>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </LoadState>
    <MintTokenModal :vault="vault" :ns="ns" v-model:open="minting" />
    <AlertModal :open="revoking !== null" @update:open="closeRevoke">
      <AlertModalContent class="flex max-h-[calc(100dvh-2rem)] flex-col">
        <ModalHeader>
          <ModalTitle>Revoke {{ revoking?.name }}?</ModalTitle>
          <ModalDescription>
            Later reads with this token fail. Products retain loaded values
            until they restart.
          </ModalDescription>
        </ModalHeader>
        <ModalBody class="-mx-1 min-h-0 flex-1 overflow-y-auto px-1" v-if="changeError"
          ><Alert severity="danger" :description="changeError.message"
        /></ModalBody>
        <ModalFooter>
          <Button variant="outline" tone="neutral" @click="closeRevoke(false)"
            >Cancel</Button
          >
          <Button tone="danger" :is-loading="changing" @click="revoke"
            >Revoke</Button
          >
        </ModalFooter>
      </AlertModalContent>
    </AlertModal>
  </div>
</template>
