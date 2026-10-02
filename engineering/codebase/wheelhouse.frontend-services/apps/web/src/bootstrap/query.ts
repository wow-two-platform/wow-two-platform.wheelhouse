import { createQueryClient, useQueryCache } from '@wow-two-beta/ui-vue/query';

export { useAppQuery, useAppQueries, useAppMutation, useQueryCache, useRefresh, queryPlugin } from '@wow-two-beta/ui-vue/query';

/** Owns the operator session's in-memory server cache. */
export const queryClient = createQueryClient();

/** Cancels late callbacks and clears private records when the operator session changes. */
export function clearQuerySession(): void {
  queryClient.invalidateSession();
}

/** Pins the cache invalidation command used by Wheelhouse's application composables. */
export function useInvalidate() {
  return useQueryCache().invalidate;
}
