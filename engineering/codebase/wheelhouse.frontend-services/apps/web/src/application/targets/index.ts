import { computed, toValue, type MaybeRefOrGetter } from "vue";
import { useAppMutation, useAppQuery } from "@/bootstrap/query";
import type { SaveTargetRequest } from "@/domain/targets";
import { targetsApi } from "@/integration/targets";
import { ProductKeys } from "@/application/products";
import { DeploymentKeys } from "@/application/deployments/DeploymentKeys";

/** Query keys for targets; inventory writes invalidate through these. */
export const TargetKeys = {
  all: ["targets"] as const,
  list: (product: string | null) => ["targets", product ?? "*"] as const,
};

/** One product's targets, or every target, with the operator's confirmed edits. */
export function useTargets(product: MaybeRefOrGetter<string | null>) {
  const list = useAppQuery({
    key: () => TargetKeys.list(toValue(product)),
    queryFn: ({ signal }) => targetsApi.listTargets(toValue(product), signal),
  });
  // A target changes what products list as environments and what the deployment views offer.
  const invalidates = () => [TargetKeys.all, ProductKeys.list, DeploymentKeys.targets];
  const create = useAppMutation({
    mutationFn: (body: SaveTargetRequest, { signal }) => targetsApi.createTarget(body, signal),
    invalidates,
    meta: { suppressGlobalError: true },
  });
  const update = useAppMutation({
    mutationFn: ({ slug, body }: { slug: string; body: SaveTargetRequest }, { signal }) =>
      targetsApi.updateTarget(slug, body, signal),
    invalidates,
    meta: { suppressGlobalError: true },
  });
  const remove = useAppMutation({
    mutationFn: (slug: string, { signal }) => targetsApi.deleteTarget(slug, signal),
    invalidates,
    meta: { suppressGlobalError: true },
  });

  return {
    targets: computed(() => list.data.value ?? []),
    loading: list.loading,
    create: (body: SaveTargetRequest) => create.mutateAsync(body),
    update: (slug: string, body: SaveTargetRequest) => update.mutateAsync({ slug, body }),
    remove: (slug: string) => remove.mutateAsync(slug),
  };
}

/** Operations the target form receives. */
export type TargetOperations = ReturnType<typeof useTargets>;
