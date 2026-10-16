<script lang="ts">
/** Secret metadata and rotation signals for the selected namespace. */
export interface SecretsTableProps {
  vault: string;
  ns: string;
  overdue: ReadonlySet<string>;
}
/** @internal Identifies an add or rotation editor without storing a secret value. */
interface SecretEditor {
  key?: string;
}
</script>

<script setup lang="ts">
import { computed, shallowRef, watch } from "vue";
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
import {
  Alert,
  DataRegion,
  SkeletonStateSlot,
  StatusIndicator,
  useSkeletonItems,
} from "@wow-two-beta/ui-vue/presentation/feedback";
import { useSecretChanges, useVaultSecrets } from "@/application/secrets";
import { SecretState } from "@/domain/secrets";
import SetSecretModal from "./SetSecretModal.vue";

/** Shows metadata and controls serving state without exposing stored values. */
defineOptions({ name: "SecretsTable" });
const props = defineProps<SecretsTableProps>();
defineSlots<{}>();

const { data, error, isRefreshing } = useVaultSecrets(
  () => props.vault,
  () => props.ns,
);
const changes = useSecretChanges(
  () => props.vault,
  () => props.ns,
);
const changeError = changes.error;
const changing = changes.loading;
const editing = shallowRef<SecretEditor | null>(null);

/** True until the first read, including while no namespace is selected yet: the table draws placeholder rows. */
const pending = computed(() => data.value === undefined && !error.value);
/** The secrets, or placeholder rows until the namespace's first read arrives. */
const rows = useSkeletonItems(() => data.value, 3);

/** Discards an editor when its vault or namespace changes. */
watch([() => props.vault, () => props.ns], () => {
  editing.value = null;
});

/** Dismisses the local add or rotation editor. */
function closeEditor(open: boolean): void {
  if (!open) editing.value = null;
}
</script>

<template>
  <div class="flex flex-col gap-4">
    <div class="flex flex-wrap items-center justify-between gap-3">
      <p class="text-sm text-muted-foreground">
        Only metadata is readable. Rotate a key to write its next version.
      </p>
      <Button size="sm" :is-disabled="!props.ns" @click="editing = {}">
        <template #leading><Plus :size="14" /></template>Add secret
      </Button>
    </div>
    <Alert
      v-if="changeError"
      severity="danger"
      :description="changeError.message"
    />
    <DataRegion
      :is-first-load="pending"
      :is-refreshing="isRefreshing"
      :error="error"
      :has-data="data !== undefined"
      :is-empty="data !== undefined && !data.length"
      empty-title="No secrets yet"
      empty-description="Add the values this product environment needs."
      loading-label="Loading secrets"
    >
      <Table
        density="compact"
        is-hoverable
        container-class-name="overflow-x-auto rounded-xl border border-border"
      >
        <TableHead
          ><TableRow>
            <TableHeaderCell>Key</TableHeaderCell
            ><TableHeaderCell>State</TableHeaderCell>
            <TableHeaderCell>Version</TableHeaderCell
            ><TableHeaderCell>Updated</TableHeaderCell>
            <TableHeaderCell
              ><span class="sr-only">Actions</span></TableHeaderCell
            >
          </TableRow></TableHead
        >
        <TableBody>
          <TableRow v-for="(secret, index) in rows" :key="secret?.key ?? `placeholder-${index}`">
            <!-- A placeholder row leaves the key cell empty, so it draws one bar. -->
            <TableCell>
              <span v-if="secret" class="block min-w-0">
                <span class="block font-mono text-xs">{{ secret.key }}</span>
                <span
                  v-if="secret.description"
                  class="block text-xs text-muted-foreground"
                  >{{ secret.description }}</span
                >
              </span>
            </TableCell>
            <TableCell>
              <SkeletonStateSlot
                ><StatusIndicator
                  :tone="
                    secret?.state === SecretState.Disabled ? 'warning' : 'success'
                  "
                  :label="secret?.state ?? SecretState.Active"
              /></SkeletonStateSlot>
            </TableCell>
            <TableCell
              ><SkeletonStateSlot>v{{ secret?.version ?? 1 }}</SkeletonStateSlot></TableCell
            >
            <TableCell class="whitespace-nowrap text-xs text-muted-foreground">
              <span class="flex flex-col items-start gap-1">
                <SkeletonStateSlot
                  ><RelativeTime v-if="secret" :value="secret.updatedAtUtc" /><template v-else
                    >2 days ago</template
                  ></SkeletonStateSlot
                >
                <Badge v-if="secret && overdue.has(secret.key)" variant="warning"
                  >rotation due</Badge
                >
              </span>
            </TableCell>
            <TableCell label="" class="justify-end">
              <div class="flex justify-end gap-1">
                <Button
                  variant="ghost"
                  tone="neutral"
                  size="sm"
                  :aria-label="`Rotate ${secret?.key ?? 'secret'}`"
                  title="Write a new version"
                  @click="secret && (editing = { key: secret.key })"
                >
                  <KeyRound :size="14" />
                </Button>
                <Button
                  variant="ghost"
                  :tone="
                    secret?.state === SecretState.Disabled ? 'neutral' : 'danger'
                  "
                  size="sm"
                  :is-disabled="changing"
                  @click="
                    secret &&
                      changes.setDisabled(
                        secret.key,
                        secret.state !== SecretState.Disabled,
                      )
                  "
                >
                  {{
                    secret?.state === SecretState.Disabled ? "Enable" : "Disable"
                  }}
                </Button>
              </div>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </DataRegion>
    <SetSecretModal
      :vault="vault"
      :ns="ns"
      :secret-key="editing?.key"
      :open="editing !== null"
      @update:open="closeEditor"
    />
  </div>
</template>
