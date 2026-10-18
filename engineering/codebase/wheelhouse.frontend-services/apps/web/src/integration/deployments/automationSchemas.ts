import { z } from 'zod';
import { DeploymentCause } from '@/domain/deployments';

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
