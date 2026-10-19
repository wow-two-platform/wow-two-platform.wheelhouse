import { computed, shallowRef, toValue, watch, type MaybeRefOrGetter } from "vue";
import { useAppMutation, useAppQuery, useInvalidate } from "@/bootstrap/query";
import { DeploymentKeys } from "@/application/deployments/DeploymentKeys";
import { TargetKeys } from "@/application/targets";
import { InfraJobRules, JOB_POLL_MS, type StartPlacementRequest } from "@/domain/infrastructure";
import { placementsApi } from "@/integration/infrastructure";
import { InfrastructureKeys } from "./InfrastructureKeys";

/**
 * A product's placements, newest first, read every two seconds while one is queued or running. When a placement seen
 * in flight finishes, the environments and the tailnet status are read again.
 */
export function usePlacements(product: MaybeRefOrGetter<string | null>) {
  const invalidate = useInvalidate();
  /** Mirrors whether one runs; the query reads its options before the read exists. @internal */
  const active = shallowRef(false);
  const query = useAppQuery({
    key: () => InfrastructureKeys.placements(toValue(product) ?? ""),
    queryFn: ({ signal }) => placementsApi.listPlacements(toValue(product) ?? "", signal),
    enabled: () => Boolean(toValue(product)),
    refetchInterval: () => (active.value ? JOB_POLL_MS : false),
    meta: { suppressGlobalError: true },
  });
  watch(
    () => new Map((query.data.value ?? []).map((placement) => [placement.id, placement.status] as const)),
    (next, previous) => {
      active.value = InfraJobRules.anyActive([...next.values()].map((status) => ({ status })));
      if (!previous) return;
      const finished = [...next].some(([id, status]) => InfraJobRules.hasFinished(previous.get(id), status));
      if (!finished) return;
      void invalidate(TargetKeys.all);
      void invalidate(DeploymentKeys.targets);
      void invalidate(InfrastructureKeys.tailnet);
    },
    { immediate: true },
  );
  return { ...query, placements: computed(() => query.data.value ?? []) };
}

/** Places a product on a server; the placements are read again, so the new job shows and is followed. */
export function usePlacementChanges() {
  const place = useAppMutation({
    mutationFn: ({ product, body }: { product: string; body: StartPlacementRequest }, { signal }) =>
      placementsApi.place(product, body, signal),
    invalidates: ({ product }) => [InfrastructureKeys.placements(product)],
    meta: { suppressGlobalError: true },
  });
  return { place: (product: string, body: StartPlacementRequest) => place.mutateAsync({ product, body }) };
}

/** Operations the placement dialog receives. */
export type PlacementOperations = ReturnType<typeof usePlacementChanges>;
