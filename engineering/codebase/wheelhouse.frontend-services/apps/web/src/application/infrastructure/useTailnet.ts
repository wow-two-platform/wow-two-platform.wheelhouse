import { toValue, type MaybeRefOrGetter } from "vue";
import { useAppMutation, useAppQuery } from "@/bootstrap/query";
import { tailnetApi } from "@/integration/infrastructure";
import { InfrastructureKeys } from "./InfrastructureKeys";

/** How often the tailnet status, with each node's online state, is read again. */
const TAILNET_REFRESH_MS = 60_000;

/**
 * Whether Wheelhouse reaches Tailscale and the nodes it reads there. Every reader shares one read; the one that polls
 * (the Tailnet panel) reads it again every minute, so the many server panels never multiply the Tailscale calls.
 */
export function useTailnet(poll = false) {
  return useAppQuery({
    key: InfrastructureKeys.tailnet,
    queryFn: ({ signal }) => tailnetApi.getStatus(signal),
    refetchInterval: poll ? TAILNET_REFRESH_MS : false,
    meta: { suppressGlobalError: true },
  });
}

/** The live policy beside the rendered one, read while the review is open; failures show in the review. */
export function usePolicyReview(enabled: MaybeRefOrGetter<boolean>) {
  return useAppQuery({
    key: InfrastructureKeys.policy,
    queryFn: ({ signal }) => tailnetApi.getPolicy(signal),
    enabled: () => toValue(enabled),
    meta: { suppressGlobalError: true },
  });
}

/** Applies the reviewed policy or puts back the previous one; both read the review and the status again. */
export function usePolicyChanges() {
  const invalidates = () => [InfrastructureKeys.policy, InfrastructureKeys.tailnet];
  const apply = useAppMutation({
    mutationFn: (body: { etag: string; digest: string }, { signal }) => tailnetApi.applyPolicy(body, signal),
    invalidates,
    meta: { suppressGlobalError: true },
  });
  const revert = useAppMutation({
    mutationFn: (body: { etag: string }, { signal }) => tailnetApi.revertPolicy(body, signal),
    invalidates,
    meta: { suppressGlobalError: true },
  });
  return {
    apply: (body: { etag: string; digest: string }) => apply.mutateAsync(body),
    revert: (body: { etag: string }) => revert.mutateAsync(body),
  };
}
