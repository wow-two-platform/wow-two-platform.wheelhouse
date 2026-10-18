import { z } from 'zod';
import { DeploymentIssueKind, DeploymentOutcome, JobStatus, StepStatus } from '@/domain/deployments';

const nullableText = z.string().nullable();
// Site links open in the operator's browser, so only http(s) addresses survive decoding.
const siteUrl = z.string().url().refine((value) => /^https?:\/\//.test(value), 'Site links must be http(s)');

const CurrentStepSchema = z.object({
  name: z.string(),
  status: nullableText.optional(),
  startedAt: nullableText.optional(),
  index: z.number().int().nullable().optional(),
});

export const DeploymentItemSchema = z.object({
  id: z.string(),
  targetId: nullableText.default(null),
  environment: nullableText.default(null),
  product: nullableText.default(null),
  workspace: nullableText.default(null),
  release: nullableText.default(null),
  bundleId: nullableText.default(null),
  kind: z.enum(['release', 'candidate']).nullable().catch(null).default(null),
  branch: nullableText.default(null),
  sourceCommit: nullableText.default(null),
  actor: nullableText.default(null),
  status: z.enum(JobStatus).catch(JobStatus.Unknown),
  outcome: z.enum(DeploymentOutcome).catch(DeploymentOutcome.Unknown),
  reason: nullableText.default(null),
  warnings: z.array(z.string()).default([]),
  submittedAt: z.string(),
  startedAt: nullableText.default(null),
  completedAt: nullableText.default(null),
  durationSeconds: z.number().nullable().default(null),
  changedServices: z.array(z.string()).nullable().default(null),
  currentStep: CurrentStepSchema.nullable().default(null),
});

export const DeploymentPageSchema = z.object({
  items: z.array(DeploymentItemSchema),
  nextCursor: nullableText.default(null),
});

export const DeploymentActivitySchema = z.object({
  from: z.string(),
  to: z.string(),
  timeZone: z.string(),
  days: z.array(z.object({ date: z.string(), succeeded: z.number().int(), failed: z.number().int() })),
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
});

export const DeploymentRangeStatsSchema = z.object({
  current: RangeMeasuresSchema,
  previous: RangeMeasuresSchema.nullable(),
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
});
