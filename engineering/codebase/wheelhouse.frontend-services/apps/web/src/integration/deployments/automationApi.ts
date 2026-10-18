import type {
  BuildFailure,
  BuildUsage,
  DeployFreeze,
  DeploymentAutomation,
  ProductAutomation,
  ReleaseChangelog,
  ReleaseFacts,
} from '@/domain/deployments';
import { requestData, requestEmpty } from '@/integration/common';
import {
  BuildFailureSchema,
  BuildUsageSchema,
  DeployFreezeSchema,
  DeploymentAutomationSchema,
  ProductAutomationSchema,
  ReleaseChangelogSchema,
  ReleaseFactsSchema,
} from './automationSchemas';

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

  /** What a release's bundle declares that a deploy should weigh; downloads it into the host's cache. */
  getReleaseFacts: (release: string, signal?: AbortSignal) =>
    requestData<ReleaseFacts>(`/api/deployments/releases/${encodeURIComponent(release)}/facts`, ReleaseFactsSchema, { signal }),

  /** What changed between what a target runs (`from`, null when nothing) and the release to promote. */
  getChangelog: (product: string, from: string | null, to: string, signal?: AbortSignal) => {
    const query = new URLSearchParams([...(from ? [['from', from]] : []), ['to', to]]).toString();
    return requestData<ReleaseChangelog>(
      `/api/deployments/products/${encodeURIComponent(product)}/changelog?${query}`,
      ReleaseChangelogSchema,
      { signal },
    );
  },

  /** The products whose newest finished main build failed; `params` are the history filter's entity parameters. */
  listBuildFailures: (params: ReadonlyArray<readonly [string, string]>, signal?: AbortSignal) => {
    const query = new URLSearchParams(params.map(([key, value]) => [key, value])).toString();
    return requestData<BuildFailure[]>(`/api/deployments/issues/builds${query ? `?${query}` : ''}`, BuildFailureSchema.array(), {
      signal,
    });
  },

  /** What a product's builds cost over the last 30 days; read from GitHub at most once an hour. */
  getUsage: (product: string, signal?: AbortSignal) =>
    requestData<BuildUsage>(`/api/deployments/products/${encodeURIComponent(product)}/usage`, BuildUsageSchema, { signal }),

  /** Every hold on deploys. */
  listFreezes: (signal?: AbortSignal) =>
    requestData<DeployFreeze[]>('/api/deployments/freezes', DeployFreezeSchema.array(), { signal }),

  /** Holds deploys: everywhere without a product, to one product, or to one product's environment. */
  freeze: (scope: FreezeRequest, signal?: AbortSignal) =>
    requestData<DeployFreeze>('/api/deployments/freezes', DeployFreezeSchema, {
      signal,
      method: 'POST',
      action: 'freeze',
      body: {
        ...(scope.product ? { product: scope.product } : {}),
        ...(scope.environment ? { environment: scope.environment } : {}),
        ...(scope.reason ? { reason: scope.reason } : {}),
      },
    }),

  /** Lifts the hold on one scope, named the way it was frozen. */
  unfreeze: (scope: Omit<FreezeRequest, 'reason'>, signal?: AbortSignal) => {
    const query = new URLSearchParams([
      ...(scope.product ? [['product', scope.product]] : []),
      ...(scope.environment ? [['environment', scope.environment]] : []),
    ]).toString();
    return requestEmpty(`/api/deployments/freezes${query ? `?${query}` : ''}`, { signal, method: 'DELETE', action: 'freeze' });
  },
};

/** The scope a freeze holds and why. */
export interface FreezeRequest {
  readonly product?: string | null;
  readonly environment?: string | null;
  readonly reason?: string | null;
}
