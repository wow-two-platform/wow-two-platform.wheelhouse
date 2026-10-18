import { computed, toValue, type MaybeRefOrGetter } from "vue";
import { useRoute, useRouter } from "vue-router";

import {
  DeploymentView,
  mergeDeploymentQuery,
  readDeploymentQuery,
  type DeploymentViewState,
} from "@/application/deployments";
import { EntityKeys, type DeploymentFilterState } from "@/domain/deployments";

/**
 * A deployments view held in the address: entities, status, range, Grid or List and the expanded day, so a link
 * reproduces what the owner sees. With `lockedProduct`, the entities are that product alone and the address keeps
 * none of its own.
 */
export function useDeploymentViewRoute(lockedProduct: MaybeRefOrGetter<string | null> = null) {
  const route = useRoute();
  const router = useRouter();

  const state = computed<DeploymentViewState>(() => {
    const read = readDeploymentQuery(route.query);
    const locked = toValue(lockedProduct);
    return locked ? { ...read, filter: { ...read.filter, entities: [EntityKeys.product(locked)] } } : read;
  });

  /** Replaces the view in the address; history keeps one entry per page, not one per filter click. */
  function update(next: { view?: DeploymentView; day?: string | null; filter?: Partial<DeploymentFilterState> }): void {
    const merged: DeploymentViewState = {
      view: next.view ?? state.value.view,
      day: next.day === undefined ? state.value.day : next.day,
      filter: { ...state.value.filter, ...next.filter },
    };
    const written = toValue(lockedProduct) ? { ...merged, filter: { ...merged.filter, entities: [] } } : merged;
    void router.replace({ path: route.path, query: mergeDeploymentQuery(route.query, written), hash: route.hash });
  }

  return {
    state,
    filter: computed(() => state.value.filter),
    view: computed(() => state.value.view),
    day: computed(() => state.value.day),
    /** Changes the filter; the expanded day closes, since it belonged to the old filter's calendar. */
    setFilter: (filter: Partial<DeploymentFilterState>) => update({ filter, day: null }),
    setView: (view: DeploymentView) => update({ view }),
    setDay: (day: string | null) => update({ day }),
  };
}
