import { computed } from "vue";
import { useAppMutation, useAppQuery } from "@/bootstrap/query";
import type { CreateIntegrationKeyRequest } from "@/domain/integrations";
import { integrationKeysApi } from "@/integration/integrations";

const IntegrationKeyKeys = { list: ["integration-keys"] as const };

/** The integration keys, with confirmed creation and revocation. */
export function useIntegrationKeys() {
  const list = useAppQuery({
    key: IntegrationKeyKeys.list,
    queryFn: ({ signal }) => integrationKeysApi.listKeys(signal),
  });
  const invalidates = () => [IntegrationKeyKeys.list];
  const create = useAppMutation({
    mutationFn: (body: CreateIntegrationKeyRequest, { signal }) =>
      integrationKeysApi.createKey(body, signal),
    invalidates,
    meta: { suppressGlobalError: true },
  });
  const revoke = useAppMutation({
    mutationFn: (id: string, { signal }) => integrationKeysApi.revokeKey(id, signal),
    invalidates,
    meta: { suppressGlobalError: true },
  });
  const keys = computed(() => list.data.value ?? []);
  const error = computed(() => list.error.value?.message ?? null);

  return {
    keys,
    loading: list.loading,
    /** True until the first list arrives: the page renders placeholder rows. */
    isFirstLoad: list.isFirstLoad,
    /** Whether a list has arrived, so a later failure keeps it on screen. */
    hasData: computed(() => list.data.value !== undefined),
    error,
    reload: async () => {
      await list.refetch();
    },
    create: (body: CreateIntegrationKeyRequest) => create.mutateAsync(body),
    revoke: (id: string) => revoke.mutateAsync(id),
  };
}

/** Operations the key-creation dialog receives. */
export type IntegrationKeyOperations = ReturnType<typeof useIntegrationKeys>;
