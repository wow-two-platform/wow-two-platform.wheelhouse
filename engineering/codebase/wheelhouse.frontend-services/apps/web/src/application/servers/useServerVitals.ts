import { useAppQuery } from "@/bootstrap/query";
import { serversApi } from "@/integration/servers";
import { ServerKeys } from "./ServerKeys";

const REFRESH_MS = 60_000;

/**
 * Reads the shared server snapshot and polls it every minute while the page is visible. The SDK interval joins a
 * read already in flight instead of cancelling it, so a slow SSH snapshot is never restarted.
 */
export function useServerVitals() {
  return useAppQuery({
    key: ServerKeys.vitals,
    queryFn: ({ signal }) => serversApi.getVitals(signal),
    refetchInterval: REFRESH_MS,
    meta: { suppressGlobalError: true },
  });
}
