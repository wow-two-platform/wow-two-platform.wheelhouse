import { computed, toValue, type MaybeRefOrGetter } from 'vue';

import { useAppMutation, useAppQuery } from '@/bootstrap/query';
import { DefaultAutomation, type ProductAutomation } from '@/domain/deployments';
import { deploymentAutomationApi } from '@/integration/deployments/automationApi';

/** Query keys for what the control plane deploys on its own. */
export const AutomationKeys = {
  all: ['deployments', 'automation'] as const,
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
