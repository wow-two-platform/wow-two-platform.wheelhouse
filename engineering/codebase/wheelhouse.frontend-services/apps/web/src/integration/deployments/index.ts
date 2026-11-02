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
import type { ControlOperation, QueuedControl } from "@/domain/deployments/controls";
import type { MigrationCheck } from "@/domain/deployments/migrations";
import { requestData } from "@/integration/common";
import {
  BuildRequestSchema,
  ControlSubmittedSchema,
  CommitEntrySchema,
  DeploymentJobSchema,
  DeploymentStatsSchema,
  DeploymentTargetSchema,
  MigrationCheckSchema,
  ReleaseArtifactSchema,
  RunRevealSchema,
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

  /** Check now: `migrate check` from the image the target runs, write-free; up to about 90 seconds, refused while a
   *  rollout runs. */
  checkMigrations: (target: string, signal?: AbortSignal) =>
    requestData<MigrationCheck>(
      `/api/deployments/targets/${encodeURIComponent(target)}/migrations`,
      MigrationCheckSchema,
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

  /** Stops, starts or restarts the release a target runs: `service`, or the whole environment without one; a reset
   *  takes its database, or only the migration set `set` names; a run executes `command` — its split arguments — once
   *  in a one-off container of `service`'s release. Prod needs `confirm`, its target ID typed, and a `reason`. */
  control: (
    target: string,
    body: {
      readonly operation: ControlOperation;
      readonly service?: string | null | undefined;
      readonly set?: string | null | undefined;
      readonly command?: readonly string[] | null | undefined;
      readonly reason?: string | undefined;
      readonly confirm?: string | undefined;
    },
    signal?: AbortSignal,
  ) =>
    requestData<QueuedControl>(`/api/deployments/targets/${encodeURIComponent(target)}/controls`, ControlSubmittedSchema, {
      signal,
      method: "POST",
      action: "control",
      body: {
        operation: body.operation,
        ...(body.service ? { service: body.service } : {}),
        ...(body.set ? { set: body.set } : {}),
        ...(body.command?.length ? { command: body.command } : {}),
        ...(body.reason?.trim() ? { reason: body.reason.trim() } : {}),
        ...(body.confirm ? { confirm: body.confirm } : {}),
      },
    }),

  /** Reads a run's last line as its command printed it, once: the target removes it with this read, so a second read
   *  or one past its time is refused. The caller shows it and keeps no copy. */
  revealRun: (target: string, job: string, signal?: AbortSignal) =>
    requestData<{ line: string }>(
      `/api/deployments/targets/${encodeURIComponent(target)}/controls/${encodeURIComponent(job)}/reveal`,
      RunRevealSchema,
      { signal, method: "POST", action: "control" },
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
