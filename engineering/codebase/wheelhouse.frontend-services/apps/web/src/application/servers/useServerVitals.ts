import { effectScope, getCurrentInstance, onScopeDispose, type App, type EffectScope } from "vue";
import { useAppQuery } from "@/bootstrap/query";
import { serversApi } from "@/integration/servers";
import { ServerKeys } from "./ServerKeys";

const REFRESH_MS = 60_000;

/** Whether the next snapshot read asks the console to read the hosts now. @internal */
let liveNext = false;

/**
 * Has the next read of the server snapshot read the hosts now instead of the snapshot the console holds for a minute
 * for every tab: an explicit Refresh, or a change the owner just made (a finished rollout or control, an enrolled
 * server). Polling reads the held snapshot, so open tabs cost the hosts one read a minute between them.
 */
export function requestLiveVitals() {
  liveNext = true;
}

/** Reads the server snapshot and polls it every minute while the page is visible. @internal */
function readVitals() {
  return useAppQuery({
    key: ServerKeys.vitals,
    queryFn: ({ signal }) => {
      const fresh = liveNext;
      liveNext = false;
      return serversApi.getVitals(signal, fresh);
    },
    refetchInterval: REFRESH_MS,
    meta: { suppressGlobalError: true },
  });
}

/** One app's vitals subscription and the panels reading it. @internal */
interface SharedVitals {
  readonly scope: EffectScope;
  readonly query: ReturnType<typeof readVitals>;
  readers: number;
}

const Subscriptions = new WeakMap<App, SharedVitals>();

/**
 * Reads the shared server snapshot and polls it every minute while the page is visible. Every panel of an app reads
 * one subscription, so the snapshot loads once and polls on one timer; it stops when the last panel unmounts. The SDK
 * interval joins a read already in flight instead of cancelling it, so a slow SSH snapshot is never restarted.
 */
export function useServerVitals() {
  const app = getCurrentInstance()?.appContext.app;
  if (!app) return readVitals();
  let shared = Subscriptions.get(app);
  if (!shared) {
    // Detached, so the subscription outlives the panel that started it while another still reads it.
    const scope = effectScope(true);
    shared = { scope, query: scope.run(readVitals)!, readers: 0 };
    Subscriptions.set(app, shared);
  }
  const current = shared;
  current.readers++;
  onScopeDispose(() => {
    if (--current.readers > 0) return;
    current.scope.stop();
    if (Subscriptions.get(app) === current) Subscriptions.delete(app);
  });
  return current.query;
}
