import { computed } from "vue";
import { useAppMutation, useAppQuery } from "@/bootstrap/query";
import type { SaveVaultRequest } from "@/domain/secrets";
import { secretsApi } from "@/integration/secrets";
import { ProductKeys } from "@/application/products";
import { SecretKeys } from "./SecretKeys";

/** The vault definitions the inventory editor holds, with the operator's confirmed edits. */
export function useVaultDefinitions() {
  const list = useAppQuery({
    key: SecretKeys.definitions,
    queryFn: ({ signal }) => secretsApi.listDefinitions(signal),
  });
  // A vault decides which namespace each product environment's settings belong in.
  const invalidates = () => [SecretKeys.vaults, ProductKeys.list];
  const create = useAppMutation({
    mutationFn: (body: SaveVaultRequest, { signal }) => secretsApi.createVault(body, signal),
    invalidates,
    meta: { suppressGlobalError: true },
  });
  const update = useAppMutation({
    mutationFn: ({ slug, body }: { slug: string; body: SaveVaultRequest }, { signal }) =>
      secretsApi.updateVault(slug, body, signal),
    invalidates,
    meta: { suppressGlobalError: true },
  });
  const remove = useAppMutation({
    mutationFn: (slug: string, { signal }) => secretsApi.deleteVault(slug, signal),
    invalidates,
    meta: { suppressGlobalError: true },
  });

  return {
    vaults: computed(() => list.data.value ?? []),
    loading: list.loading,
    create: (body: SaveVaultRequest) => create.mutateAsync(body),
    update: (slug: string, body: SaveVaultRequest) => update.mutateAsync({ slug, body }),
    remove: (slug: string) => remove.mutateAsync(slug),
  };
}

/** Operations the vault form receives. */
export type VaultOperations = ReturnType<typeof useVaultDefinitions>;
