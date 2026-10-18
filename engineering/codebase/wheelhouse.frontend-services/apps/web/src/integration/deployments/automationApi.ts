import type { DeploymentAutomation, ProductAutomation } from '@/domain/deployments';
import { requestData } from '@/integration/common';
import { DeploymentAutomationSchema, ProductAutomationSchema } from './automationSchemas';

/** What the control plane deploys on its own; every write carries its action header. */
export const deploymentAutomationApi = {
  /** Every product's automation choices and the automatic deploys waiting. */
  get: (signal?: AbortSignal) =>
    requestData<DeploymentAutomation>('/api/deployments/automation', DeploymentAutomationSchema, { signal }),

  /** Changes a product's choices; an omitted one stays as it is. */
  update: (product: string, change: Partial<Pick<ProductAutomation, 'autoDeployDev' | 'autoPromote'>>, signal?: AbortSignal) =>
    requestData<ProductAutomation>(`/api/deployments/automation/${encodeURIComponent(product)}`, ProductAutomationSchema, {
      signal,
      method: 'PUT',
      action: 'automation',
      body: change,
    }),
};
