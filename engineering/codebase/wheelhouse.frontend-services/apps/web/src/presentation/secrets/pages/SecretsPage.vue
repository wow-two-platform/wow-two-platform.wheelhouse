<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { KeyRound } from "lucide-vue-next";
import { Button } from "@wow-two-beta/ui-vue/presentation/actions";
import {
  Badge,
  EmptyState,
  TabsGroup,
  TabsGroupList,
  TabsGroupTab,
  TabsGroupPanel,
} from "@wow-two-beta/ui-vue/presentation/display";
import { Alert } from "@wow-two-beta/ui-vue/presentation/feedback";
import {
  SelectPicker,
  SelectPickerTrigger,
  SelectPickerValue,
  SelectPickerContent,
  SelectPickerItem,
} from "@wow-two-beta/ui-vue/presentation/forms";
import {
  SecretKeys,
  useVaultHygiene,
  useVaultNamespaces,
  useVaults,
} from "@/application/secrets";
import { useInvalidate } from "@/bootstrap/query";
import { useRefresh } from "@/bootstrap/query";
import { VaultStatus } from "@/domain/secrets";
import { SkeletonStateGroup, SkeletonStateSlot } from "@wow-two-beta/ui-vue/presentation/feedback";
import { LoadState, Panel, PageActions, RefreshButton } from "@/presentation/common/components";
import NamespaceList from "../components/NamespaceList.vue";
import SecretsTable from "../components/SecretsTable.vue";
import TokensTable from "../components/TokensTable.vue";

/** Presents vault availability, namespace selection, write-only secrets, and token administration. */
defineOptions({ name: "SecretsPage" });
defineSlots<{}>();

/** @internal Maps live vault availability to its visible status. */
const VaultTone = {
  unsealed: "success",
  sealed: "warning",
  unreachable: "danger",
} as const;
const { data: vaults, error } = useVaults();
const invalidate = useInvalidate();
const { refresh, refreshing } = useRefresh(() => invalidate(SecretKeys.vaults));
const vault = ref("");
const namespace = ref("");
const selected = computed(() =>
  vaults.value?.find((item) => item.id === vault.value),
);
const hygiene = useVaultHygiene(() =>
  selected.value?.status === VaultStatus.Unsealed ? vault.value : "",
);
/* The same query the namespace rail runs (one cache entry): the detail pane waits on it for its first shape. */
const namespaces = useVaultNamespaces(() =>
  selected.value?.status === VaultStatus.Unsealed ? vault.value : "",
);
/** True until the first vault catalog and its namespaces arrive: the frame renders with placeholder values. */
const pending = computed(
  () =>
    vaults.value === undefined ||
    (namespaces.data.value === undefined && !namespaces.error.value),
);
const hygieneData = hygiene.data;
const hygieneError = hygiene.error;
const hygieneLoading = hygiene.loading;
/** The keys overdue for rotation in the selected namespace. */
const overdueSecrets = computed(
  () =>
    new Set(
      (hygieneData.value?.overdueSecrets ?? [])
        .filter((secret) => secret.namespace === namespace.value)
        .map((secret) => secret.key),
    ),
);
/** The token warnings scoped to the selected namespace. */
const tokenFlags = computed(
  () =>
    new Map(
      (hygieneData.value?.overdueTokens ?? [])
        .filter((token) => token.namespace === namespace.value)
        .map((token) => [token.id, token.reason]),
    ),
);
const dueCount = computed(
  () =>
    (hygieneData.value?.overdueSecrets.length ?? 0) +
    (hygieneData.value?.overdueTokens.length ?? 0),
);

/** Chooses an available vault when the catalog loads or removes the current vault. */
watch(
  vaults,
  (items) => {
    if (items && !items.some((item) => item.id === vault.value))
      vault.value = items[0]?.id ?? "";
  },
  { immediate: true },
);
/** Resets the namespace immediately when its owning vault changes. */
watch(
  vault,
  () => {
    namespace.value = "";
  },
  { flush: "sync" },
);

/** Changes the authoritative vault selection. */
function selectVault(value: unknown): void {
  vault.value = typeof value === "string" ? value : "";
}
</script>

<template>
  <PageActions>
    <RefreshButton :refreshing="refreshing" @refresh="refresh" />
  </PageActions>
  <LoadState
    :loading="false"
    :error="error"
    :has-data="vaults !== undefined"
    :empty="vaults !== undefined && !vaults.length"
    empty-title="No vaults configured"
    empty-description="Configured vaults appear here when they are available."
    @retry="refresh"
  >
    <SkeletonStateGroup
      class="contents"
      :is-loading="vaults === undefined || refreshing"
      label="Loading vaults"
    >
    <Panel
      :title="selected?.name ?? 'Vault'"
      description="Select a namespace to manage its write-only secrets and product access tokens."
    >
      <template #title
        ><SkeletonStateSlot :is-loading="vaults === undefined">{{
          selected?.name ?? "Local vault"
        }}</SkeletonStateSlot></template
      >
      <template #actions>
        <SkeletonStateSlot shape="circle"
          ><Badge :variant="selected ? VaultTone[selected.status] : 'success'">{{
            selected?.status ?? "unsealed"
          }}</Badge></SkeletonStateSlot
        >
        <SelectPicker
          v-if="(vaults?.length ?? 0) > 1"
          :model-value="vault || null"
          :get-option-label="
            (id) => vaults?.find((item) => item.id === id)?.name ?? String(id)
          "
          @update:model-value="selectVault"
        >
          <SelectPickerTrigger size="sm" aria-label="Vault">
            <SelectPickerValue placeholder="Select a vault" />
          </SelectPickerTrigger>
          <SelectPickerContent>
            <SelectPickerItem
              v-for="item in vaults ?? []"
              :key="item.id"
              :item-key="item.id"
              :label="item.name"
            />
          </SelectPickerContent>
        </SelectPicker>
      </template>
      <EmptyState
        v-if="selected && selected.status !== VaultStatus.Unsealed"
        size="sm"
        :title="`This vault is ${selected.status}`"
        description="Unseal the vault or restore its connection to manage its namespaces."
      >
        <template #icon><KeyRound :size="24" /></template>
      </EmptyState>
      <div v-else :key="vault || 'pending'" class="flex flex-col gap-5">
        <Alert
          v-if="hygieneError"
          severity="warning"
          title="Rotation status unavailable"
          :description="hygieneError.message"
        >
          <template #actions>
            <Button
              size="sm"
              variant="ghost"
              tone="neutral"
              @click="hygiene.refetch()"
              >Retry</Button
            >
          </template>
        </Alert>
        <SkeletonStateGroup
          v-else
          class="flex flex-wrap items-center gap-2 text-xs text-muted-foreground"
          :is-loading="!hygieneData || hygieneLoading || refreshing"
          label="Checking rotation status"
        >
          <SkeletonStateSlot shape="circle"
            ><Badge :variant="dueCount > 0 ? 'warning' : 'success'">
              {{
                dueCount > 0
                  ? `${dueCount} rotation issue${dueCount === 1 ? "" : "s"}`
                  : "Rotation up to date"
              }}
            </Badge></SkeletonStateSlot
          >
          <SkeletonStateSlot
            >{{ hygieneData?.secrets ?? 0 }} secrets ·
            {{ hygieneData?.tokens ?? 0 }} tokens across this vault</SkeletonStateSlot
          >
        </SkeletonStateGroup>
        <div
          class="grid min-h-96 overflow-hidden rounded-2xl border border-border lg:grid-cols-[15rem_minmax(0,1fr)]"
        >
          <aside
            class="border-b border-border bg-muted/40 p-4 lg:border-b-0 lg:border-r"
          >
            <NamespaceList
              :vault="vault"
              :selected="namespace"
              @select="namespace = $event"
            />
          </aside>
          <section class="min-w-0 bg-card p-4 lg:p-5">
            <template v-if="namespace || pending">
              <div class="mb-5 flex flex-wrap items-center gap-2">
                <KeyRound :size="16" class="text-primary" />
                <h2 class="break-all font-mono text-sm font-semibold">
                  <SkeletonStateSlot :is-loading="!namespace">{{
                    namespace || "namespace"
                  }}</SkeletonStateSlot>
                </h2>
              </div>
              <TabsGroup
                :key="`${vault}/${namespace}`"
                default-value="secrets"
                class="min-w-0"
              >
                <TabsGroupList aria-label="Namespace administration">
                  <TabsGroupTab value="secrets">Secrets</TabsGroupTab>
                  <TabsGroupTab value="tokens">Tokens</TabsGroupTab>
                </TabsGroupList>
                <TabsGroupPanel value="secrets" class="pt-5">
                  <SecretsTable
                    :vault="vault"
                    :ns="namespace"
                    :overdue="overdueSecrets"
                  />
                </TabsGroupPanel>
                <TabsGroupPanel value="tokens" class="pt-5">
                  <TokensTable
                    :vault="vault"
                    :ns="namespace"
                    :flags="tokenFlags"
                  />
                </TabsGroupPanel>
              </TabsGroup>
            </template>
            <EmptyState
              v-else
              size="sm"
              title="Select a namespace"
              description="Its secrets and product tokens open here."
            >
              <template #icon><KeyRound :size="24" /></template>
            </EmptyState>
          </section>
        </div>
      </div>
    </Panel>
    </SkeletonStateGroup>
  </LoadState>
</template>
