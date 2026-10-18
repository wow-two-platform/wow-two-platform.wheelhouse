import type {
  DeploymentActivity,
  DeploymentDetail,
  DeploymentIssue,
  DeploymentItem,
  DeploymentPage,
  DeploymentPromotion,
  DeploymentRangeStats,
} from '@/domain/deployments';
import { requestData } from '@/integration/common';
import {
  DeploymentActivitySchema,
  DeploymentDetailSchema,
  DeploymentIssueSchema,
  DeploymentItemSchema,
  DeploymentPageSchema,
  DeploymentPromotionSchema,
  DeploymentRangeStatsSchema,
} from './historySchemas';

/** Query parameters as `[key, value]` pairs; repeated keys stay repeated. */
export type QueryParams = ReadonlyArray<readonly [string, string]>;

/** The path with its query; `URLSearchParams` encodes `+` in offsets and `/` in services safely. @internal */
function withQuery(path: string, params: QueryParams): string {
  const query = new URLSearchParams(params.map(([key, value]) => [key, value])).toString();
  return query ? `${path}?${query}` : path;
}

/** The deployment history reads behind the Deployments page and a product's Deployments tab. */
export const deploymentHistoryApi = {
  /** One page of the filter's deployments, newest first; `cursor` continues a previous page. */
  listPage: (params: QueryParams, cursor: string | null, limit: number, signal?: AbortSignal) =>
    requestData<DeploymentPage>(
      withQuery('/api/deployments/history', [...params, ...(cursor ? [['cursor', cursor] as const] : []), ['limit', String(limit)]]),
      DeploymentPageSchema,
      { signal },
    ),

  /** Finished rollouts per local day in the IANA time zone `tz`. */
  getActivity: (params: QueryParams, tz: string, signal?: AbortSignal) =>
    requestData<DeploymentActivity>(withQuery('/api/deployments/history/activity', [...params, ['tz', tz]]), DeploymentActivitySchema, {
      signal,
    }),

  /** The filter's measures beside the range before it. */
  getStats: (params: QueryParams, signal?: AbortSignal) =>
    requestData<DeploymentRangeStats>(withQuery('/api/deployments/history/stats', params), DeploymentRangeStatsSchema, { signal }),

  /** Live rollouts with the step each is on. */
  listRunning: (params: QueryParams, signal?: AbortSignal) =>
    requestData<DeploymentItem[]>(withQuery('/api/deployments/running', params), DeploymentItemSchema.array(), { signal }),

  /** Per product, the newest release that succeeded on dev and is not what prod runs. */
  listPromotions: (params: QueryParams, signal?: AbortSignal) =>
    requestData<DeploymentPromotion[]>(withQuery('/api/deployments/promotions', params), DeploymentPromotionSchema.array(), {
      signal,
    }),

  /** The targets that need attention: a failure with no success since, or a rollout live too long. */
  listIssues: (params: QueryParams, signal?: AbortSignal) =>
    requestData<DeploymentIssue[]>(withQuery('/api/deployments/issues', params), DeploymentIssueSchema.array(), { signal }),

  /** One deployment in full, from local records. */
  getDetail: (id: string, signal?: AbortSignal) =>
    requestData<DeploymentDetail>(`/api/deployments/${encodeURIComponent(id)}/detail`, DeploymentDetailSchema, { signal }),
};
