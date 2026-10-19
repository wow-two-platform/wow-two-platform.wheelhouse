<script setup lang="ts">
import { computed } from "vue";
import { ChevronLeft, KeyRound } from "lucide-vue-next";
import { Button } from "@wow-two-beta/fe/presentation/actions";
import {
  Badge,
  EmptyState,
  TabsGroup,
  TabsGroupList,
  TabsGroupTab,
  TabsGroupPanel,
} from "@wow-two-beta/fe/presentation/display";
import {
  Alert,
  DataRegion,
  SkeletonStateGroup,
  SkeletonStateSlot,
} from "@wow-two-beta/fe/presentation/feedback";
import {
  SelectPicker,
  SelectPickerTrigger,
  SelectPickerValue,
  SelectPickerContent,
  SelectPickerItem,
} from "@wow-two-beta/fe/presentation/forms";
import {
  SecretKeys,
  useVaultHygiene,
  useVaultNamespaces,
  useVaults,
} from "@/application/secrets";
import { useInvalidate } from "@/bootstrap/query";
import { useRefresh } from "@/bootstrap/query";
import { VaultStatus } from "@/domain/secrets";
import {
  ListDetailLayout,
  PageHeaderActions,
} from "@wow-two-beta/fe/presentation/layout";
import { InfoPopover, Panel, RefreshButton } from "@/presentation/common/components";
import { useListDetailRoute } from "@/presentation/common/hooks";
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
const { data: vaults, error, isFirstLoad, isRefreshing } = useVaults();
const invalidate = useInvalidate();
const { refresh, refreshing } = useRefresh(() => invalidate(SecretKeys.vaults));
/* `?vault=` and `?namespace=` hold the selection; a namespace opens its detail, which replaces the list on phones. */
const listDetail = useListDetailRoute(["namespace"]);
/** The requested vault while the catalog has it; otherwise the first vault. */
const vault = computed(() => {
  const requested = listDetail.read("vault");
  const items = vaults.value ?? [];
  return items.find((item) => item.id === requested)?.id ?? items[0]?.id ?? "";
});
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
/**
 * The requested namespace, or the vault's first one when none is requested. A requested namespace the vault does not
 * have selects nothing rather than another namespace: writes go to the namespace the owner named.
 */
const namespace = computed(() => {
  const requested = listDetail.read("namespace");
  const items = namespaces.data.value;
  if (requested === null) return items?.[0]?.slug ?? "";
  return !items || items.some((item) => item.slug === requested) ? requested : "";
});
/** True until the first vault catalog and its namespaces arrive: the frame renders with placeholder values. */
const pending = computed(
  () =>
    vaults.value === undefined ||
    (namespaces.data.value === undefined && !namespaces.error.value),
);
const hygieneData = hygiene.data;
const hygieneError = hygiene.error;
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

/** Changes the vault; its namespaces differ, so the namespace selection clears with it. */
function selectVault(value: unknown): void {
  if (typeof value === "string" && value !== vault.value)
    void listDetail.select({ vault: value, namespace: null });
}

/** Opens a namespace's secrets and tokens; the vault stays as the URL names it, so back returns to that list. */
function selectNamespace(slug: string): void {
  void listDetail.select({ namespace: slug });
}
</script>

<template>
  <PageHeaderActions>
    <RefreshButton size="md" :refreshing="refreshing" @refresh="refresh" />
  </PageHeaderActions>
  <DataRegion
    :is-first-load="isFirstLoad"
    :is-refreshing="isRefreshing"
    :error="error"
    :has-data="vaults !== undefined"
    :is-empty="vaults !== undefined && !vaults.length"
    empty-title="No vaults configured"
    empty-description="Configured vaults appear here when they are available."
    loading-label="Loading vaults"
  >
    <SkeletonStateGroup class="contents" :is-loading="refreshing" label="Refreshing vaults">
    <Panel :title="selected?.name ?? 'Vault'">
      <template #info>
        <InfoPopover :title="selected?.name ?? 'Vault'">
          <p>
            Choose a namespace to manage its secrets and the access tokens products read them with. Values are
            write-only: once saved, this console never shows them again.
          </p>
        </InfoPopover>
      </template>
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
          <SelectPickerTrigger aria-label="Vault">
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
        <DataRegion
          v-else
          :is-first-load="!hygieneData"
          loading-label="Checking rotation status"
          class="flex flex-wrap items-center gap-2 text-xs text-muted-foreground"
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
        </DataRegion>
        <ListDetailLayout
          :is-detail-open="listDetail.isDetailOpen.value"
          list-width="15rem"
          back-label="Namespaces"
          back-class="mb-0 bg-card px-2 pt-2"
          class="min-h-96 overflow-hidden rounded-2xl border border-border"
          @back="listDetail.back"
        >
          <template #list>
            <aside
              class="h-full border-border bg-muted/40 p-4 lg:border-r"
            >
              <NamespaceList
                :vault="vault"
                :selected="namespace"
                @select="selectNamespace"
              />
            </aside>
          </template>
          <!-- The pane has no gutter of its own, so the phone back control sits in a bar above the namespace. -->
          <template #back="{ back, label }">
            <Button variant="ghost" tone="neutral" size="sm" @click="back"
              ><template #leading><ChevronLeft :size="16" /></template>{{ label }}</Button
            >
          </template>
          <template #detail>
            <section class="h-full min-w-0 bg-card p-4 lg:p-5">
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
          </template>
        </ListDetailLayout>
      </div>
    </Panel>
    </SkeletonStateGroup>
  </DataRegion>
</template>
