import { toValue, type MaybeRefOrGetter } from "vue";
import { useAppQuery } from "@/bootstrap/query";
import { topologyApi } from "@/integration/topology";
import { TopologyKeys } from "./TopologyKeys";

/** Tracks the selected target's saved Compose declaration through the shared session cache. */
export function useTargetTopology(target: MaybeRefOrGetter<string | null>) {
  return useAppQuery({
    key: () => TopologyKeys.target(toValue(target) ?? ""),
    queryFn: ({ signal }) =>
      topologyApi.getTargetTopology(toValue(target) ?? "", signal),
    enabled: () => Boolean(toValue(target)),
  });
}
