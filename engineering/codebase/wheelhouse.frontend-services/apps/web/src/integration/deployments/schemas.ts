import { z } from "zod";
import { JobStatus, StepFindingKind, StepStatus, TargetCondition } from "@/domain/deployments";
import { ControlOperation } from "@/domain/deployments/controls";

const jobStatus = z.enum(JobStatus);
// Site links open in the owner's browser, so only http(s) URLs survive decoding.
const siteUrl = z
  .string()
  .url()
  .refine((value) => /^https?:\/\//.test(value), "Site links must be http(s)");
const PublishedSiteSchema = z.object({
  name: z.string(),
  service: z.string(),
  exposure: z.enum(["public", "private"]),
  url: siteUrl,
  probe: z
    .object({
      ok: z.boolean(),
      status: z.number().int().optional(),
      detail: z.string().optional(),
    })
    .optional(),
});
/** One finding a config check step names, by setting and rule code; a finding this console cannot read is dropped. */
const StepFindingSchema = z.object({
  kind: z.enum(StepFindingKind),
  setting: z.string().nullish().transform((value) => value ?? null),
  code: z.string().nullish().transform((value) => value ?? null),
  count: z.number().int().nonnegative().nullish().transform((value) => value ?? null),
});
/** A config check step's findings; absent on other steps, and a malformed list reads as absent. */
export const StepFindingsSchema = z
  .array(z.unknown())
  .transform((items) => items.flatMap((item) => {
    const finding = StepFindingSchema.safeParse(item);
    return finding.success ? [finding.data] : [];
  }))
  .nullish()
  .catch(undefined)
  .transform((value) => value ?? undefined);
const DeploymentStepSchema = z.object({
  name: z.string(),
  status: z.enum(StepStatus),
  startedAt: z.string().optional(),
  completedAt: z.string().optional(),
  detail: z.string().optional(),
  findings: StepFindingsSchema,
});
const nullableText = z.string().nullable();
/** What the owner keeps stopped on a target. */
export const TargetStopSchema = z.object({
  environment: z.boolean(),
  services: z.array(z.string()).default([]),
  at: z.string().nullish(),
  actor: z.string().nullish(),
});
/** A control — a stop, start, restart, reset or run — as its record names it; a set reset names its migration set, and
 * a run its command as a masked label. */
export const DeploymentOperationSchema = z.object({
  kind: z.enum(ControlOperation),
  services: z.array(z.string()).default([]),
  set: z.string().nullish(),
  command: z.string().nullish(),
});
/** What a run kept on its job: how its command ended, with the exit code and the last output line. */
export const ControlRunSchema = z.object({
  service: z.string(),
  command: z.string(),
  status: z.enum(["succeeded", "failed", "timed out"]),
  exitCode: z.number().int().nullable(),
  seconds: z.number(),
  lastLine: z.string().nullable(),
  reveal: z.object({ expiresAt: z.string() }).nullish().catch(null),
});
/** A run's last line as its command printed it, read once. */
export const RunRevealSchema = z.object({ line: z.string() });
/** What a control queued: its submission and the release it acts on. */
export const ControlSubmittedSchema = z.object({
  id: z.string(),
  targetId: z.string(),
  operation: DeploymentOperationSchema,
  release: z.string().nullish(),
  status: jobStatus,
});
/** How a rollout treats each service: what the release changes, its dependency waves and how each service fared. */
export const ServiceRolloutSchema = z.object({
  staged: z.boolean().default(false),
  waves: z.array(z.array(z.string())).default([]),
  removed: z.array(z.string()).default([]),
  services: z
    .array(
      z.object({
        name: z.string(),
        change: z.string(),
        outcome: z.string(),
        dependsOn: z.array(z.string()).default([]),
        from: nullableText.optional(),
        to: nullableText.optional(),
        reason: nullableText.optional(),
        health: nullableText.optional(),
        at: nullableText.optional(),
      }),
    )
    .default([]),
});
export const DeploymentJobSchema = z.object({
  id: z.string(),
  status: jobStatus,
  targetId: nullableText.optional(),
  bundleId: nullableText.optional(),
  release: nullableText.optional(),
  actor: nullableText.optional(),
  submittedAt: z.string().optional(),
  reason: nullableText.optional(),
  failure: nullableText.optional(),
  startedAt: z.string().optional(),
  completedAt: z.string().optional(),
  mutationStarted: z.boolean().optional(),
  steps: z.array(DeploymentStepSchema).optional(),
  warnings: z.array(z.string()).optional(),
  sites: z.array(PublishedSiteSchema).optional(),
  // The runner's record carries it once the rollout is planned; a malformed one never fails the outcome read.
  serviceRollout: ServiceRolloutSchema.nullish().catch(null),
  operation: DeploymentOperationSchema.nullish().catch(null),
  // A run's job carries how its command ended; a malformed one never fails the outcome read.
  run: ControlRunSchema.nullish().catch(null),
});
export const DeploymentTargetSchema = z.object({
  id: z.string(),
  product: z.string(),
  environment: z.string(),
  serverId: z.string(),
  provider: z.string(),
  host: z.string(),
  acceptsCandidates: z.boolean().default(false),
  needsConfirmation: z.boolean().default(false),
  requiresDevPass: z.boolean().default(false),
  self: z.boolean().default(false),
});
export const SelfDeploymentRunSchema = z.object({
  id: z.string(),
  release: z.string().nullable(),
  previousRelease: z.string().nullable(),
  status: z.string(),
  outcome: z.string(),
  actor: z.string().nullable(),
  reason: z.string().nullable(),
  failure: z.string().nullable(),
  submittedAt: z.string(),
  completedAt: z.string().nullable(),
  beforeBoot: z.boolean(),
});
export const SelfDeploymentSchema = z.object({
  environment: z.string(),
  target: z.string().nullable(),
  bootedAt: z.string(),
  latest: SelfDeploymentRunSchema.nullable(),
});
const releaseKind = z.enum(["release", "candidate"]);
/** One repository's release inside a release set (decision 70); the catalog's entries name their commit `commit`. */
export const ReleaseComponentSchema = z
  .object({
    name: nullableText.optional(),
    repository: nullableText.optional(),
    release: nullableText.optional(),
    kind: releaseKind.nullish(),
    sourceCommit: nullableText.optional(),
    commit: nullableText.optional(),
  })
  .transform(({ commit, ...component }) => ({
    name: component.name ?? null,
    repository: component.repository ?? null,
    release: component.release ?? null,
    kind: component.kind ?? null,
    sourceCommit: component.sourceCommit ?? commit ?? null,
  }));
/** A repository of the product a release set does not hold yet. */
export const NotYetSchema = z
  .object({ name: nullableText.optional(), repository: nullableText.optional() })
  .transform((item) => ({ name: item.name ?? null, repository: item.repository ?? null }));
export const ReleaseArtifactSchema = z.object({
  id: z.string(),
  product: z.string(),
  release: z.string(),
  kind: releaseKind.default("release"),
  prerelease: z.boolean(),
  publishedAt: z.string(),
  provider: z.string(),
  commit: nullableText.optional(),
  branch: nullableText.optional(),
  expiresAt: nullableText.optional(),
  components: z.array(ReleaseComponentSchema).nullish(),
  notYet: z.array(NotYetSchema).nullish(),
});
const ServiceVersionSchema = z.object({
  version: z.string(),
  changedIn: z.string(),
});
export const CommitEntrySchema = z.object({
  sha: z.string().regex(/^[a-f0-9]{40}$/),
  message: z.string(),
  author: z.string(),
  date: nullableText.optional(),
  buildId: z.string().nullable(),
  canBuild: z.boolean().optional(),
  url: z
    .string()
    .regex(/^https:\/\/github\.com\/[^/]+\/[^/]+\/commit\/[a-f0-9]{40}$/)
    .optional(),
});
export const ServiceLogsSchema = z.object({
  targetId: z.string(),
  service: z.string(),
  tail: z.number().int(),
  collectedAt: z.string(),
  lines: z.array(z.string()),
  truncated: z.boolean().default(false),
});
export const BuildRequestSchema = z.object({
  product: z.string(),
  commit: z.string(),
  status: z.literal("requested"),
});
const RolloutSchema = z.object({
  id: z.string(),
  release: z.string().optional(),
  status: jobStatus.optional(),
  actor: z.string().optional(),
  startedAt: z.string().optional(),
  completedAt: z.string().optional(),
  reason: z.string().optional(),
  mutationStarted: z.boolean().optional(),
  kind: releaseKind.optional(),
  branch: z.string().optional(),
  sourceCommit: z.string().optional(),
  versions: z.record(z.string(), ServiceVersionSchema).optional(),
  sites: z.array(PublishedSiteSchema).optional(),
  steps: z.array(DeploymentStepSchema).optional(),
  warnings: z.array(z.string()).optional(),
  operation: DeploymentOperationSchema.nullish().catch(null),
});
/** A field the runner may leave out or not read: absent, null and an unexpected shape all read as null. */
const readText = z.string().nullish().catch(null);
const readLevel = z.number().int().nullish().catch(null);
/** One migration set as the target last read it (migrations spec M38). */
const MigrationSetReadingSchema = z.object({
  name: z.string(),
  schema: readText,
  service: z.string(),
  level: readLevel,
  newestLabel: readText,
  newestKind: readText,
  state: readText,
  rollbackFloor: readText,
  release: readText,
  job: readText,
  source: z.string(),
  at: z.string(),
});
export const TargetMigrationsSchema = z.object({
  updatedAt: readText,
  sets: z.array(MigrationSetReadingSchema),
});
export const TargetStateSchema = z.object({
  targetId: z.string(),
  project: z.string(),
  condition: z.enum(TargetCondition),
  current: RolloutSchema.nullable(),
  active: RolloutSchema.nullable(),
  stopped: TargetStopSchema.nullish().catch(null),
  // A runner that never ran a migrate step, or predates them, has none; a shape this console cannot read drops it.
  migrations: TargetMigrationsSchema.nullish().catch(null),
});
/** What Check now answers: each migrate item of the release the target runs, and the sets with the check merged in. */
export const MigrationCheckSchema = z.object({
  targetId: z.string(),
  project: readText,
  release: readText,
  checkedAt: readText,
  items: z.array(
    z.object({
      service: z.string(),
      verb: readText,
      release: readText,
      status: z.string(),
      exitCode: readLevel,
      seconds: z.number().nullish().catch(null),
      detail: readText,
      sets: z
        .array(
          z.object({
            name: z.string(),
            schema: readText,
            state: readText,
            levelBefore: readLevel,
            levelAfter: readLevel,
            applied: z.array(z.object({ label: z.string(), kind: readText })).catch([]),
            rollbackFloor: readText,
          }),
        )
        .catch([]),
    }),
  ),
  migrations: TargetMigrationsSchema.nullish().catch(null),
});
export const TargetCheckSchema = z
  .object({
    targetId: z.string(),
    ok: z.boolean(),
    checks: z.array(
      z.object({ name: z.string(), ok: z.boolean(), detail: z.string() }),
    ),
    // The runner embeds state before the transport adds the outer target identifier.
    state: TargetStateSchema.omit({ targetId: true })
      .extend({ targetId: z.string().optional() })
      .optional(),
  })
  .transform((result) => ({
    ...result,
    ...(result.state
      ? {
          state: {
            ...result.state,
            targetId: result.state.targetId ?? result.targetId,
          },
        }
      : {}),
  }));
const MeasuresSchema = z.object({
  since: z.string(),
  deploys: z.number(),
  succeeded: z.number(),
  failed: z.number(),
  refused: z.number(),
  pending: z.number(),
  successRate: z.number().nullable(),
  medianRolloutSeconds: z.number().nullable(),
  medianRecoverySeconds: z.number().nullable(),
});
export const DeploymentStatsSchema = MeasuresSchema.extend({
  windowDays: z.number(),
  previous: MeasuresSchema.nullable(),
  daily: z.array(
    z.object({
      date: z.string(),
      succeeded: z.number(),
      failed: z.number(),
      refused: z.number(),
    }),
  ),
  targets: z.array(
    z.object({
      targetId: z.string(),
      deploys: z.number(),
      succeeded: z.number(),
      failed: z.number(),
      lastStatus: jobStatus,
      lastRelease: nullableText.optional(),
      lastDeployAt: z.string(),
      failingSince: nullableText,
    }),
  ),
});
