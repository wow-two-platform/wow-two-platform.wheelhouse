import { z } from "zod";
import type {
  BuildRequest,
  CommitEntry,
  DeploymentJob,
  DeploymentStats,
  DeploymentTarget,
  ReleaseArtifact,
  SelfDeployment,
  ServiceLogs,
  TargetCheck,
  TargetState,
} from "@/domain/deployments";
import type { ControlOperation } from "@/domain/deployments/controls";
import { requestData } from "@/integration/common";
import {
  BuildRequestSchema,
  ControlSubmittedSchema,
  CommitEntrySchema,
  DeploymentJobSchema,
  DeploymentStatsSchema,
  DeploymentTargetSchema,
  ReleaseArtifactSchema,
  SelfDeploymentSchema,
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

  /** This console's own newest rollout, read back from the host's journal. */
  getSelfDeployment: (signal?: AbortSignal) =>
    requestData<SelfDeployment>("/api/self-deployment", SelfDeploymentSchema, { signal }),

  getOutcome: (id: string, signal?: AbortSignal) =>
    requestData<DeploymentJob>(
      `/api/deployments/${encodeURIComponent(id)}`,
      DeploymentJobSchema,
      { signal },
    ),

  /** `confirm` is the typed target ID that local prod and a skipped dev pass require; `extra.reason` is the owner's
   *  one-line why, kept on the job, and `extra.overrideFreeze` deploys although a freeze holds the target. */
  startDeployment: (
    target: string,
    release: string,
    confirm?: string,
    skipDevPass?: boolean,
    signal?: AbortSignal,
    extra: { readonly reason?: string | undefined; readonly overrideFreeze?: boolean | undefined } = {},
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
        ...(extra.reason?.trim() ? { reason: extra.reason.trim() } : {}),
        ...(extra.overrideFreeze ? { overrideFreeze: true } : {}),
      },
    }),

  /** A product's branches; `repository` names one of a product's repositories (its primary one when absent). */
  listBranches: (product: string, signal?: AbortSignal, repository?: string | null) =>
    requestData<string[]>(
      `/api/deployments/products/${encodeURIComponent(product)}/branches${repository ? `?repository=${encodeURIComponent(repository)}` : ""}`,
      z.array(z.string()),
      { signal },
    ),

  listCommits: (product: string, branch: string, signal?: AbortSignal, repository?: string | null) =>
    requestData<CommitEntry[]>(
      `/api/deployments/products/${encodeURIComponent(product)}/commits?branch=${encodeURIComponent(branch)}${repository ? `&repository=${encodeURIComponent(repository)}` : ""}`,
      CommitEntrySchema.array(),
      { signal },
    ),

  /** Starts the build workflow of one of the product's repositories (its primary one when absent) for a commit that has
   * no build yet. */
  requestBuild: (product: string, commit: string, signal?: AbortSignal, repository?: string | null) =>
    requestData<BuildRequest>(
      `/api/deployments/products/${encodeURIComponent(product)}/builds`,
      BuildRequestSchema,
      { signal, method: "POST", action: "build", body: { commit, ...(repository ? { repository } : {}) } },
    ),

  /** Stops, starts or restarts the release a target runs: `service`, or the whole environment without one. Prod needs
   *  `confirm`, its target ID typed, and a `reason`. */
  control: (
    target: string,
    body: {
      readonly operation: ControlOperation;
      readonly service?: string | null | undefined;
      readonly reason?: string | undefined;
      readonly confirm?: string | undefined;
    },
    signal?: AbortSignal,
  ) =>
    requestData(`/api/deployments/targets/${encodeURIComponent(target)}/controls`, ControlSubmittedSchema, {
      signal,
      method: "POST",
      action: "control",
      body: {
        operation: body.operation,
        ...(body.service ? { service: body.service } : {}),
        ...(body.reason?.trim() ? { reason: body.reason.trim() } : {}),
        ...(body.confirm ? { confirm: body.confirm } : {}),
      },
    }),

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
