import { z } from 'zod';
import { Attention, DeployState, ServiceResult } from '@/domain/common';
import { DeploymentIssueKind, DeploymentOutcome, DeploymentStates, JobStatus, StepStatus } from '@/domain/deployments';
import { DeploymentOperationSchema, ReleaseComponentSchema } from './schemas';

const nullableText = z.string().nullable();
// Site links open in the owner's browser, so only http(s) addresses survive decoding.
const siteUrl = z.string().url().refine((value) => /^https?:\/\//.test(value), 'Site links must be http(s)');

const CurrentStepSchema = z.object({
  name: z.string(),
  status: nullableText.optional(),
  startedAt: nullableText.optional(),
  index: z.number().int().nullable().optional(),
});

/** A watch window's status; `superseded` is the word older records carry for `replaced`. */
const WatchStatusSchema = z
  .enum(['watching', 'passed', 'degraded', 'replaced', 'superseded', 'unverified'])
  .catch('unverified')
  .transform((value) => (value === 'superseded' ? 'replaced' : value));

const CauseSchema = z.object({
  kind: z.string(),
  origin: nullableText.default(null),
  message: nullableText.default(null),
  at: nullableText.default(null),
});

const EffectSchema = z.object({
  service: z.string(),
  finding: z.string(),
  via: z.string(),
  at: nullableText.default(null),
});

const ServiceResultSchema = z.object({
  service: z.string(),
  result: z.enum(ServiceResult).catch(ServiceResult.Unknown),
  origin: z.boolean().default(false),
  from: nullableText.default(null),
  to: nullableText.default(null),
  health: nullableText.default(null),
  reason: nullableText.default(null),
});

const DeploymentItemFields = z.object({
  id: z.string(),
  targetId: nullableText.default(null),
  environment: nullableText.default(null),
  product: nullableText.default(null),
  workspace: nullableText.default(null),
  release: nullableText.default(null),
  bundleId: nullableText.default(null),
  /** Each repository's release when the release is a set of a product built from several (decision 70). */
  components: z.array(ReleaseComponentSchema).nullable().catch(null).default(null),
  kind: z.enum(['release', 'candidate']).nullable().catch(null).default(null),
  branch: nullableText.default(null),
  sourceCommit: nullableText.default(null),
  actor: nullableText.default(null),
  deployReason: nullableText.default(null),
  status: z.enum(JobStatus).catch(JobStatus.Unknown),
  outcome: z.enum(DeploymentOutcome).catch(DeploymentOutcome.Unknown),
  state: z.enum(DeployState).optional().catch(undefined),
  substate: nullableText.optional().catch(undefined),
  attention: z.enum(Attention).optional().catch(undefined),
  cause: CauseSchema.nullable().default(null).catch(null),
  effects: z.array(EffectSchema).default([]).catch([]),
  services: z.array(ServiceResultSchema).nullable().default(null).catch(null),
  reason: nullableText.default(null),
  warnings: z.array(z.string()).default([]),
  submittedAt: z.string(),
  startedAt: nullableText.default(null),
  completedAt: nullableText.default(null),
  durationSeconds: z.number().nullable().default(null),
  changedServices: z.array(z.string()).nullable().default(null),
  currentStep: CurrentStepSchema.nullable().default(null),
  watch: z
    .object({
      status: WatchStatusSchema,
      until: nullableText.default(null),
      checks: z.number().int().nullable().default(null),
      findings: z.array(z.string()).default([]),
      degradedAt: nullableText.default(null),
      completedAt: nullableText.default(null),
    })
    .nullable()
    .default(null),
  settingsChanged: z
    .array(
      z.object({
        key: z.string(),
        kind: z.enum(['setting', 'secret']).catch('setting'),
        change: z.enum(['added', 'changed', 'removed']).catch('changed'),
        from: z.number().int().nullable().optional().transform((value) => value ?? null),
        to: z.number().int().nullable().optional().transform((value) => value ?? null),
      }),
    )
    .nullable()
    .optional()
    .catch(null)
    .transform((value) => value ?? null),
  settingsSuspect: z.boolean().optional().catch(false).transform((value) => value ?? false),
  /** Set when the item is a control — a stop, start or restart of the release its target runs. */
  operation: DeploymentOperationSchema.nullish().catch(null).transform((value) => value ?? null),
});

/** A deployment as lists read it; an answer from before the status taxonomy reads its state from the runner status. */
export const DeploymentItemSchema = DeploymentItemFields.transform((item) => {
  const known = item.state !== undefined && item.attention !== undefined;
  const legacy = known ? null : DeploymentStates.legacy(item.status, item.watch);
  return {
    ...item,
    state: legacy?.state ?? item.state!,
    substate: legacy ? legacy.substate : (item.substate ?? null),
    attention: legacy?.attention ?? item.attention!,
  };
});

export const DeploymentPageSchema = z.object({
  items: z.array(DeploymentItemSchema),
  nextCursor: nullableText.default(null),
});

export const DeploymentActivitySchema = z.object({
  from: z.string(),
  to: z.string(),
  timeZone: z.string(),
  days: z.array(
    z
      .object({
        date: z.string(),
        succeeded: z.number().int(),
        failed: z.number().int(),
        calm: z.number().int().optional(),
        look: z.number().int().optional(),
        act: z.number().int().optional(),
        notApplied: z.number().int().default(0),
      })
      // An answer from before the status taxonomy: a success needed nothing, a failure needs action.
      .transform((day) => ({
        ...day,
        calm: day.calm ?? day.succeeded,
        look: day.look ?? 0,
        act: day.act ?? day.failed,
      })),
  ),
  products: z.array(z.object({ product: z.string(), succeeded: z.number().int(), failed: z.number().int() })).default([]),
});

const RangeMeasuresSchema = z.object({
  from: z.string(),
  to: z.string(),
  deploys: z.number(),
  succeeded: z.number(),
  failed: z.number(),
  refused: z.number(),
  pending: z.number(),
  successRate: z.number().nullable(),
  medianRolloutSeconds: z.number().nullable(),
  medianRecoverySeconds: z.number().nullable(),
  notApplied: z.number().default(0),
  look: z.number().default(0),
  act: z.number().default(0),
  superseded: z.number().default(0),
});

export const DeploymentRangeStatsSchema = z.object({
  current: RangeMeasuresSchema,
  previous: RangeMeasuresSchema.nullable(),
});

/** A release's dev check; a status the console does not know reads as failed, as the host's gate does. */
const VerificationSchema = z.object({
  status: z.enum(['passed', 'failed']).catch('failed'),
  service: nullableText.default(null),
  completedAt: nullableText.default(null),
  seconds: z.number().nullable().default(null),
  exitCode: z.number().int().nullable().default(null),
  detail: nullableText.default(null),
  summary: nullableText.default(null),
});

export const DeploymentPromotionSchema = z.object({
  product: z.string(),
  workspace: nullableText.default(null),
  release: z.string(),
  bundleId: z.string(),
  publishedAt: nullableText.default(null),
  devTargetId: z.string(),
  devSucceededAt: z.string(),
  prodTargetId: z.string(),
  prodRelease: nullableText.default(null),
  rollbackCompatible: z.boolean().nullable().default(null),
  hasMigrations: z.boolean().nullable().default(null),
  verifies: z.boolean().nullable().default(null),
  verification: VerificationSchema.nullable().default(null),
  components: z.array(ReleaseComponentSchema).nullable().catch(null).default(null),
});

export const DeploymentIssueSchema = z.object({
  kind: z.enum(DeploymentIssueKind).catch(DeploymentIssueKind.Failed),
  needsReconcile: z.boolean().default(false),
  at: z.string(),
  deployment: DeploymentItemSchema,
});

const ReferenceSchema = z.object({ id: z.string(), release: nullableText.default(null), bundleId: nullableText.default(null) });

export const DeploymentDetailSchema = z.object({
  deployment: DeploymentItemSchema,
  services: z
    .array(z.object({ service: z.string(), from: nullableText, to: nullableText, changed: z.boolean() }))
    .nullable()
    .default(null),
  previous: ReferenceSchema.nullable().default(null),
  rollback: ReferenceSchema.nullable().default(null),
  steps: z
    .array(
      z.object({
        name: z.string(),
        status: z.enum(StepStatus).catch(StepStatus.Skipped),
        startedAt: z.string().nullable().optional().transform((value) => value ?? undefined),
        completedAt: z.string().nullable().optional().transform((value) => value ?? undefined),
        detail: z.string().nullable().optional().transform((value) => value ?? undefined),
      }),
    )
    .default([]),
  sites: z
    .array(
      z.object({
        name: z.string(),
        service: nullableText.default(null),
        exposure: nullableText.default(null),
        url: siteUrl,
        probe: z
          .object({
            ok: z.boolean(),
            status: z.number().int().nullable().optional().transform((value) => value ?? undefined),
            detail: z.string().nullable().optional().transform((value) => value ?? undefined),
          })
          .nullable()
          .default(null),
      }),
    )
    .default([]),
  reconciledBy: nullableText.default(null),
  observed: z.boolean().default(false),
  snapshot: z
    .object({
      path: z.string(),
      createdAt: nullableText.default(null),
      databases: z
        .array(
          z.object({
            service: nullableText.default(null),
            host: z.string(),
            port: z.number().int(),
            database: z.string(),
            file: z.string(),
            bytes: z.number().nullable().default(null),
          }),
        )
        .default([]),
      volumes: z.array(z.object({ volume: z.string(), file: z.string(), bytes: z.number().nullable().default(null) })).default([]),
      restore: z.array(z.string()).default([]),
    })
    .nullable()
    .default(null),
  serviceRollout: z
    .object({
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
    })
    .nullable()
    .default(null),
});
