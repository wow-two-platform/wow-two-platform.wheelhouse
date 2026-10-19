import { computed } from "vue";
import { useAppQuery } from "@/bootstrap/query";
import { ConsolePeerRules } from "@/domain/system";
import { systemApi } from "@/integration/system";

/** Whether the management API answers, which version it runs and whether it drives the rehearsal rig. */
export function useApiConnection() {
  const { data, error } = useAppQuery({
    key: ["system", "status"],
    queryFn: ({ signal }) => systemApi.getStatus(signal),
    meta: { suppressGlobalError: true },
  });
  const connection = computed(() =>
    error.value ? "offline" : data.value ? "online" : "checking",
  );
  const localRig = computed(() => data.value?.localRig === true);
  const version = computed(() => data.value?.version ?? null);
  /** The environment this console serves, once the status answers. */
  const environment = computed(() => data.value?.environment ?? null);
  return { connection, localRig, version, environment } as const;
}

/**
 * The other console the environment tag switches to, once the owner's session reads it. Null while it loads, when the
 * host names no peer, and when the read fails: the tag then stays as it was, and a failure never toasts.
 */
export function useConsolePeer() {
  const { data } = useAppQuery({
    key: ["system", "peer"],
    queryFn: ({ signal }) => systemApi.getPeer(signal),
    meta: { suppressGlobalError: true },
  });
  return computed(() => ConsolePeerRules.from(data.value));
}
