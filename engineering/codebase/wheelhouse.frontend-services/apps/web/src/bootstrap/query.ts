import {
  computed,
  effectScope,
  getCurrentInstance,
  onScopeDispose,
  shallowRef,
  toValue,
  watch,
  type EffectScope,
  type MaybeRefOrGetter,
} from 'vue';
import { feedbackQueryErrors } from '@wow-two-beta/fe/feedback';
import {
  createQueryClient,
  useAppQuery,
  useQueryCache,
  type AppQueryMeta,
  type Endpoint,
  type UseAppQueryReturn,
} from '@wow-two-beta/fe/query';
import { sessionBridge } from '@/integration/common';

export {
  useAppQuery,
  useAppInfiniteQuery,
  useAppQueries,
  useAppMutation,
  useOptimisticMutation,
  useQueryCache,
  useRefresh,
  queryPlugin,
} from '@wow-two-beta/fe/query';

/**
 * Owns the signed-in session's in-memory server cache. A failed read or write becomes a toast (`App.vue` mounts the
 * host), so a region only settles into neutral copy; calls that show their failure in place opt out with
 * `meta: { suppressGlobalError: true }`. The session bridge's scope clears it when the signed-in identity changes or
 * expires; a 401 never toasts, since the expiry's one notice says it.
 */
export const queryClient = createQueryClient({ scope: sessionBridge.scope, onError: feedbackQueryErrors() });

/** Pins the cache invalidation command used by Wheelhouse's application composables. */
export function useInvalidate() {
  return useQueryCache().invalidate;
}

/**
 * Reads one endpoint per argument: each argument gets its own query, keyed and fetched by that argument alone, and a
 * null argument reads nothing. For a key several views share while one view's argument moves on, such as a target's
 * state that the deploy dialog reads only on its confirm step.
 *
 * `useAppQuery` with a reactive key keeps one fetcher for every key it passes through, and TanStack refetches a shared
 * key (an invalidation, a poll) with the options of the observer that last set them; a fetcher reading the argument
 * when it runs then fetches that observer's current argument — another target, or none
 * (`/api/deployments/targets//state`, a 404). Here a key's fetcher is bound to its argument, so any refetch of it
 * reads that argument.
 */
export function useEndpointQuery<A, TRaw>(
  argument: MaybeRefOrGetter<A | null | undefined>,
  endpoint: (argument: A) => Endpoint<TRaw>,
  meta?: AppQueryMeta,
): UseAppQueryReturn<TRaw> {
  // A later argument's query starts outside setup; the app's context gives it the query client.
  const app = getCurrentInstance()?.appContext.app;
  const current = shallowRef<UseAppQueryReturn<TRaw> | null>(null);
  let scope: EffectScope | null = null;
  watch(
    () => toValue(argument) ?? null,
    (value) => {
      scope?.stop();
      scope = null;
      current.value = null;
      if (value === null) return;
      const next = effectScope(true);
      const start = () => next.run(() => useAppQuery({ ...endpoint(value), ...(meta ? { meta } : {}) })) ?? null;
      current.value = app ? app.runWithContext(start) : start();
      scope = next;
    },
    { immediate: true },
  );
  onScopeDispose(() => scope?.stop());
  return {
    data: computed(() => current.value?.data.value),
    loading: computed(() => current.value?.loading.value ?? false),
    isFirstLoad: computed(() => current.value?.isFirstLoad.value ?? false),
    isRefreshing: computed(() => current.value?.isRefreshing.value ?? false),
    isPreviousData: computed(() => current.value?.isPreviousData.value ?? false),
    error: computed(() => current.value?.error.value ?? null),
    refetch: () => current.value?.refetch(),
  };
}
