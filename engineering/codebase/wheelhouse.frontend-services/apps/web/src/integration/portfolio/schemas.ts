import { z } from "zod";
import { RolloutOutcome } from "@/domain/portfolio";

const nullable = <T extends z.ZodType>(schema: T) => schema.nullish().transform((value) => value ?? null);

const PortfolioDeploySchema = z.object({
  id: z.string(),
  release: nullable(z.string()),
  status: z.string(),
  outcome: z.enum(RolloutOutcome).catch(RolloutOutcome.Unknown),
  actor: nullable(z.string()),
  submittedAt: z.string(),
  completedAt: nullable(z.string()),
  reason: nullable(z.string()),
});

const PortfolioEnvironmentSchema = z.object({
  environment: z.string(),
  lastDeploy: nullable(PortfolioDeploySchema),
  running: nullable(PortfolioDeploySchema),
  weekly: z.array(z.number().int().nonnegative()),
});

/** The portfolio summary's wire shape. */
export const PortfolioSummarySchema = z.object({
  timeZone: z.string(),
  weeks: z.array(z.string()),
  products: z.array(
    z.object({
      product: z.string(),
      workspace: nullable(z.string()),
      succeeded: z.number().int(),
      failed: z.number().int(),
      environments: z.array(PortfolioEnvironmentSchema),
    }),
  ),
  workspaces: z.array(
    z.object({
      workspace: z.string(),
      succeeded: z.number().int(),
      failed: z.number().int(),
      weekly: z.array(z.object({ succeeded: z.number().int(), failed: z.number().int() })),
    }),
  ),
});
