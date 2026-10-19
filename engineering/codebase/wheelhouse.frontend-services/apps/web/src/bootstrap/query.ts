import { feedbackQueryErrors } from '@wow-two-beta/fe/feedback';
import { createQueryClient, useQueryCache } from '@wow-two-beta/fe/query';
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
