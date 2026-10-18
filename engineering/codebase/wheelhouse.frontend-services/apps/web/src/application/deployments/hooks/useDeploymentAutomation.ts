import { computed, toValue, type MaybeRefOrGetter } from 'vue';

import { useAppMutation, useAppQuery } from '@/bootstrap/query';
import { DefaultAutomation, DeployFreezes, type ProductAutomation } from '@/domain/deployments';
import { deploymentAutomationApi, type FreezeRequest } from '@/integration/deployments/automationApi';

/** Query keys for what the control plane deploys on its own. */
export const AutomationKeys = {
  all: ['deployments', 'automation'] as const,
  freezes: ['deployments', 'freezes'] as const,
  facts: (release: string) => ['deployments', 'facts', release] as const,
  changelog: (product: string, from: string, to: string) => ['deployments', 'changelog', product, from, to] as const,
  builds: ['deployments', 'builds'] as const,
};

/** How often the automation choices and the queue are read while a view shows them. */
const AutomationPollMilliseconds = 30_000;

/** Every product's automation choices and the automatic deploys waiting. */
export function useDeploymentAutomation() {
  return useAppQuery({
    key: () => AutomationKeys.all,
    queryFn: ({ signal }) => deploymentAutomationApi.get(signal),
    refetchInterval: AutomationPollMilliseconds,
  });
}

/** One product's automation choices; the defaults until the host answers. */
export function useProductAutomation(product: MaybeRefOrGetter<string>) {
  const automation = useDeploymentAutomation();
  const choices = computed<ProductAutomation>(
    () =>
      automation.data.value?.products.find((item) => item.product === toValue(product)) ?? DefaultAutomation(toValue(product)),
  );
  return { automation, choices };
}

/** Changes a product's automation choices; a refusal shows as a toast. */
export function useUpdateAutomation() {
  return useAppMutation({
    mutationFn: (
      { product, change }: { product: string; change: Partial<Pick<ProductAutomation, 'autoDeployDev' | 'autoPromote'>> },
      { signal },
    ) => deploymentAutomationApi.update(product, change, signal),
    invalidates: () => [AutomationKeys.all],
  });
}

/** Every hold on deploys, read again every half minute while a view shows them. */
export function useDeployFreezes() {
  return useAppQuery({
    key: () => AutomationKeys.freezes,
    queryFn: ({ signal }) => deploymentAutomationApi.listFreezes(signal),
    refetchInterval: AutomationPollMilliseconds,
  });
}

/** The freeze covering a product's environment, the narrowest first; null while nothing holds deploys there. */
export function useCoveringFreeze(product: MaybeRefOrGetter<string | null | undefined>, environment: MaybeRefOrGetter<string | null | undefined>) {
  const freezes = useDeployFreezes();
  return computed(() => DeployFreezes.covering(freezes.data.value, toValue(product), toValue(environment)));
}

/** Holds deploys on one scope; a refusal shows as a toast. */
export function useFreezeDeploys() {
  return useAppMutation({
    mutationFn: (scope: FreezeRequest, { signal }) => deploymentAutomationApi.freeze(scope, signal),
    invalidates: () => [AutomationKeys.freezes],
  });
}

/** Lifts the hold on one scope; a refusal shows as a toast. */
export function useUnfreezeDeploys() {
  return useAppMutation({
    mutationFn: (scope: Omit<FreezeRequest, 'reason'>, { signal }) => deploymentAutomationApi.unfreeze(scope, signal),
    invalidates: () => [AutomationKeys.freezes],
  });
}

/** What a release's bundle declares that a deploy should weigh; read while `release` names one. A bundle never changes. */
export function useReleaseFacts(release: MaybeRefOrGetter<string | null | undefined>) {
  return useAppQuery({
    key: () => AutomationKeys.facts(toValue(release) ?? ''),
    queryFn: ({ signal }) => deploymentAutomationApi.getReleaseFacts(toValue(release) ?? '', signal),
    enabled: () => Boolean(toValue(release)),
    meta: { suppressGlobalError: true },
  });
}

/** What changed between what a target runs and the release to promote; read while `enabled` holds. */
export function useReleaseChangelog(
  product: MaybeRefOrGetter<string>,
  from: MaybeRefOrGetter<string | null>,
  to: MaybeRefOrGetter<string>,
  enabled: MaybeRefOrGetter<boolean> = true,
) {
  return useAppQuery({
    key: () => AutomationKeys.changelog(toValue(product), toValue(from) ?? '', toValue(to)),
    queryFn: ({ signal }) => deploymentAutomationApi.getChangelog(toValue(product), toValue(from), toValue(to), signal),
    enabled: () => toValue(enabled) && Boolean(toValue(to)),
    meta: { suppressGlobalError: true },
  });
}

/** The products whose newest finished main build failed, read every minute while the page is visible. */
export function useBuildFailures() {
  return useAppQuery({
    key: () => AutomationKeys.builds,
    queryFn: ({ signal }) => deploymentAutomationApi.listBuildFailures([], signal),
    refetchInterval: 60_000,
    meta: { suppressGlobalError: true },
  });
}
