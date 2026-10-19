import { z } from 'zod';
import { DeploymentCause, FleetItemStatus, FleetStatus, FreezeScope } from '@/domain/deployments';

export const ProductAutomationSchema = z.object({
  product: z.string(),
  autoDeployDev: z.boolean(),
  autoPromote: z.boolean(),
  ciDeployed: z.boolean().default(false),
});

export const DeploymentAutomationSchema = z.object({
  products: z.array(ProductAutomationSchema),
  queue: z
    .array(
      z.object({
        targetId: z.string(),
        product: z.string(),
        environment: z.string(),
        release: z.string(),
        bundleId: z.string(),
        publishedAt: z.string().nullable().default(null),
        cause: z.enum(DeploymentCause).catch(DeploymentCause.AutoDev),
        queuedAt: z.string(),
      }),
    )
    .default([]),
});

// Links open in the owner's browser, so only GitHub addresses survive decoding.
const githubUrl = z.string().refine((value) => value.startsWith('https://github.com/'), 'GitHub links only');

export const ReleaseChangelogSchema = z.object({
  from: z.string().nullable().default(null),
  to: z.string(),
  releases: z
    .array(
      z.object({
        release: z.string(),
        name: z.string(),
        publishedAt: z.string(),
        body: z.string().default(''),
        truncated: z.boolean().default(false),
        url: githubUrl,
      }),
    )
    .default([]),
  commits: z
    .array(z.object({ sha: z.string(), message: z.string().default(''), author: z.string().default(''), url: githubUrl }))
    .nullable()
    .default(null),
  totalCommits: z.number().int().nullable().default(null),
  migrations: z.array(z.string()).default([]),
  compareUrl: githubUrl.nullable().default(null),
  reason: z.string().nullable().default(null),
});

export const BuildFailureSchema = z.object({
  product: z.string(),
  workspace: z.string().nullable().default(null),
  runId: z.number().int(),
  conclusion: z.string().default('failure'),
  commit: z.string().nullable().default(null),
  title: z.string().default(''),
  at: z.string().nullable().default(null),
  url: githubUrl,
});

export const BuildUsageSchema = z.object({
  product: z.string(),
  since: z.string(),
  days: z.number().int(),
  actions: z.object({
    runs: z.number().int(),
    minutes: z.number(),
    workflows: z.array(z.object({ name: z.string(), minutes: z.number() })).default([]),
    complete: z.boolean().default(true),
  }),
  packages: z.array(z.object({ service: z.string(), package: z.string(), versions: z.number().int().nullable() })).default([]),
  storageBytes: z.number().nullable().default(null),
});

export const ReleaseFactsSchema = z.object({
  id: z.string(),
  product: z.string(),
  release: z.string(),
  kind: z.string().default('release'),
  sourceCommit: z.string(),
  rollbackCompatible: z.boolean(),
  migrations: z.boolean().nullable().default(null),
});

export const DeployFreezeSchema = z.object({
  id: z.string(),
  scope: z.enum(FreezeScope).catch(FreezeScope.Portfolio),
  product: z.string().nullable().default(null),
  environment: z.string().nullable().default(null),
  reason: z.string().nullable().default(null),
  actor: z.string(),
  at: z.string(),
  covers: z.string(),
});

export const FleetRolloutItemSchema = z.object({
  position: z.number().int(),
  product: z.string(),
  target: z.string(),
  bundleId: z.string(),
  release: z.string().nullable().default(null),
  fromRelease: z.string().nullable().default(null),
  status: z.enum(FleetItemStatus).catch(FleetItemStatus.Waiting),
  deploymentId: z.string().nullable().default(null),
  detail: z.string().nullable().default(null),
  startedAt: z.string().nullable().default(null),
  completedAt: z.string().nullable().default(null),
});

export const FleetRolloutSchema = z.object({
  id: z.string(),
  actor: z.string(),
  reason: z.string().nullable().default(null),
  overrideFreeze: z.boolean().default(false),
  status: z.enum(FleetStatus).catch(FleetStatus.Failed),
  createdAt: z.string(),
  completedAt: z.string().nullable().default(null),
  cancelRequestedBy: z.string().nullable().default(null),
  cancelRequestedAt: z.string().nullable().default(null),
  items: z.array(FleetRolloutItemSchema),
});
