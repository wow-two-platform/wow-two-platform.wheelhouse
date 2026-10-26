import { computed, ref, toValue, watch, type MaybeRefOrGetter } from 'vue';

import { useAppMutation, useAppQuery, useInvalidate } from '@/bootstrap/query';
import { DefaultAutomation, DeployFreezes, FleetRollouts, type ProductAutomation } from '@/domain/deployments';
import { deploymentAutomationApi, type FleetStart, type FreezeRequest } from '@/integration/deployments/automationApi';

import { DeploymentKeys } from '../DeploymentKeys';

/** Query keys for what the control plane deploys on its own. */
export const AutomationKeys = {
  all: ['deployments', 'automation'] as const,
  freezes: ['deployments', 'freezes'] as const,
  facts: (release: string) => ['deployments', 'facts', release] as const,
  changelog: (product: string, from: string, to: string) => ['deployments', 'changelog', product, from, to] as const,
  builds: ['deployments', 'builds'] as const,
  githubAccess: ['deployments', 'github-access'] as const,
  inventoryExport: ['deployments', 'inventory-export'] as const,
  usage: (product: string) => ['deployments', 'usage', product] as const,
  fleets: ['deployments', 'fleets'] as const,
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

/** What keeps the control plane from reaching GitHub, read every minute while the page is visible. */
export function useGitHubAccessIssues() {
  return useAppQuery({
    key: () => AutomationKeys.githubAccess,
    queryFn: ({ signal }) => deploymentAutomationApi.listGitHubAccess(signal),
    refetchInterval: 60_000,
    meta: { suppressGlobalError: true },
  });
}

/** Whether the newest inventory export failed, read every minute while the page is visible. */
export function useInventoryExportIssues() {
  return useAppQuery({
    key: () => AutomationKeys.inventoryExport,
    queryFn: ({ signal }) => deploymentAutomationApi.listInventoryExportIssues(signal),
    refetchInterval: 60_000,
    meta: { suppressGlobalError: true },
  });
}

/** What a product's builds cost over the last 30 days: Actions minutes and GHCR package versions. */
export function useBuildUsage(product: MaybeRefOrGetter<string | null>) {
  return useAppQuery({
    key: () => AutomationKeys.usage(toValue(product) ?? ''),
    queryFn: ({ signal }) => deploymentAutomationApi.getUsage(toValue(product) ?? '', signal),
    enabled: () => Boolean(toValue(product)),
    meta: { suppressGlobalError: true },
  });
}

/** How often fleets are read while one runs, and while none does. */
const FleetPollMilliseconds = { running: 3000, idle: 30_000 } as const;

/**
 * The newest fleet rollouts, read every 3 seconds while one runs and every half minute otherwise. When a product of the
 * running fleet ends, the history views and promotions are read again.
 */
export function useFleetRollouts() {
  const invalidate = useInvalidate();
  const running = ref(false);
  const fleets = useAppQuery({
    key: () => AutomationKeys.fleets,
    queryFn: ({ signal }) => deploymentAutomationApi.listFleets(10, signal),
    refetchInterval: () => (running.value ? FleetPollMilliseconds.running : FleetPollMilliseconds.idle),
    meta: { suppressGlobalError: true },
  });
  watch(
    () => FleetRollouts.active(fleets.data.value) !== null,
    (value) => (running.value = value),
    { immediate: true },
  );
  watch(
    () => fleets.data.value?.flatMap((fleet) => fleet.items.map((item) => `${fleet.id}:${item.position}:${item.status}`)).join(',') ?? null,
    (now, before) => {
      if (now === null || before === null || now === before) return;
      void invalidate(DeploymentKeys.historyViews);
      void invalidate(DeploymentKeys.statsAll);
    },
  );
  const active = computed(() => FleetRollouts.active(fleets.data.value));
  return { fleets, active };
}

/** Starts a fleet rollout; a refusal shows in the dialog that started it. */
export function useStartFleet() {
  return useAppMutation({
    mutationFn: (fleet: FleetStart, { signal }) => deploymentAutomationApi.startFleet(fleet, signal),
    invalidates: () => [AutomationKeys.fleets, DeploymentKeys.historyViews],
    meta: { suppressGlobalError: true },
  });
}

/** Cancels a running fleet between products; a refusal shows as a toast. */
export function useCancelFleet() {
  return useAppMutation({
    mutationFn: (id: string, { signal }) => deploymentAutomationApi.cancelFleet(id, signal),
    invalidates: () => [AutomationKeys.fleets],
  });
}
