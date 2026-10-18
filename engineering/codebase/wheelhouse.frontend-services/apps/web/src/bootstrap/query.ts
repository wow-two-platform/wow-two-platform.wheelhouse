import { feedbackQueryErrors } from '@wow-two-beta/fe/feedback';
import { createQueryClient, useQueryCache } from '@wow-two-beta/fe/query';

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
 * `meta: { suppressGlobalError: true }`.
 */
export const queryClient = createQueryClient({ onError: feedbackQueryErrors() });

/** Cancels late callbacks and clears private records when the signed-in session changes. */
export function clearQuerySession(): void {
  queryClient.invalidateSession();
}

/** Pins the cache invalidation command used by Wheelhouse's application composables. */
export function useInvalidate() {
  return useQueryCache().invalidate;
}
