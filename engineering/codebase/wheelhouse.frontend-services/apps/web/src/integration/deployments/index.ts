import { z } from "zod";
import type {
  BuildRequest,
  CommitEntry,
  DeploymentJob,
  DeploymentStats,
  DeploymentTarget,
  ReleaseArtifact,
  ServiceLogs,
  TargetCheck,
  TargetState,
} from "@/domain/deployments";
import { requestData } from "@/integration/common";
import {
  BuildRequestSchema,
  CommitEntrySchema,
  DeploymentJobSchema,
  DeploymentStatsSchema,
  DeploymentTargetSchema,
  ReleaseArtifactSchema,
  ServiceLogsSchema,
  TargetCheckSchema,
  TargetStateSchema,
} from "./schemas";

/** Deployment operations; every write carries its explicit action header. */
export const deploymentsApi = {
  listHistory: (signal?: AbortSignal) =>
    requestData<DeploymentJob[]>(
      "/api/deployments",
      DeploymentJobSchema.array(),
      { signal },
    ),

  listTargets: (signal?: AbortSignal) =>
    requestData<DeploymentTarget[]>(
      "/api/deployments/targets",
      DeploymentTargetSchema.array(),
      { signal },
    ),

  listReleases: (signal?: AbortSignal) =>
    requestData<ReleaseArtifact[]>(
      "/api/deployments/releases",
      ReleaseArtifactSchema.array(),
      { signal },
    ),

  getStats: (days: number, signal?: AbortSignal) =>
    requestData<DeploymentStats>(
      `/api/deployments/stats?days=${days}`,
      DeploymentStatsSchema,
      { signal },
    ),

  getTargetState: (target: string, signal?: AbortSignal) =>
    requestData<TargetState>(
      `/api/deployments/targets/${encodeURIComponent(target)}/state`,
      TargetStateSchema,
      { signal },
    ),

  checkTarget: (target: string, release?: string, signal?: AbortSignal) =>
    requestData<TargetCheck>(
      `/api/deployments/targets/${encodeURIComponent(target)}/check` +
        (release ? `?release=${encodeURIComponent(release)}` : ""),
      TargetCheckSchema,
      { signal },
    ),

  /** One service's recent container output; `tail` bounds the lines, 1-1000. */
  getLogs: (target: string, service: string, tail: number, signal?: AbortSignal) =>
    requestData<ServiceLogs>(
      `/api/deployments/targets/${encodeURIComponent(target)}/services/${encodeURIComponent(service)}/logs?tail=${tail}`,
      ServiceLogsSchema,
      { signal },
    ),

  getOutcome: (id: string, signal?: AbortSignal) =>
    requestData<DeploymentJob>(
      `/api/deployments/${encodeURIComponent(id)}`,
      DeploymentJobSchema,
      { signal },
    ),

  /** `confirm` is the typed target ID that local prod and a skipped dev pass require. */
  startDeployment: (
    target: string,
    release: string,
    confirm?: string,
    skipDevPass?: boolean,
    signal?: AbortSignal,
  ) =>
    requestData<DeploymentJob>("/api/deployments", DeploymentJobSchema, {
      signal,
      method: "POST",
      action: "deploy",
      body: {
        target,
        release,
        ...(confirm ? { confirm } : {}),
        ...(skipDevPass ? { skipDevPass: true } : {}),
      },
    }),

  listBranches: (product: string, signal?: AbortSignal) =>
    requestData<string[]>(
      `/api/deployments/products/${encodeURIComponent(product)}/branches`,
      z.array(z.string()),
      { signal },
    ),

  listCommits: (product: string, branch: string, signal?: AbortSignal) =>
    requestData<CommitEntry[]>(
      `/api/deployments/products/${encodeURIComponent(product)}/commits?branch=${encodeURIComponent(branch)}`,
      CommitEntrySchema.array(),
      { signal },
    ),

  /** Starts the product's build workflow for a commit that has no build yet. */
  requestBuild: (product: string, commit: string, signal?: AbortSignal) =>
    requestData<BuildRequest>(
      `/api/deployments/products/${encodeURIComponent(product)}/builds`,
      BuildRequestSchema,
      { signal, method: "POST", action: "build", body: { commit } },
    ),

  reconcile: (target: string, job: string, signal?: AbortSignal) =>
    requestData<DeploymentJob>(
      `/api/deployments/targets/${encodeURIComponent(target)}/reconcile`,
      DeploymentJobSchema,
      {
        signal,
        method: "POST",
        action: "reconcile",
        body: { job },
      },
    ),
};
