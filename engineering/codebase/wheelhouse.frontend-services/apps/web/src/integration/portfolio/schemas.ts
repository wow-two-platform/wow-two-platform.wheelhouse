import { z } from "zod";
import { Attention, DeployState } from "@/domain/common";
import { DeploymentStates } from "@/domain/deployments";
import { RolloutOutcome } from "@/domain/portfolio";
import { TargetStopSchema } from "@/integration/deployments/schemas";

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
  state: z.enum(DeployState).optional().catch(undefined),
  substate: nullable(z.string()),
  attention: z.enum(Attention).optional().catch(undefined),
  servicesOnRelease: nullable(z.number().int()),
  serviceCount: nullable(z.number().int()),
  watch: nullable(z.string()),
}).transform((deploy) => {
  // An answer from before the status taxonomy reads its state from the runner status.
  const legacy = deploy.state && deploy.attention ? null : DeploymentStates.legacy(deploy.status);
  return {
    ...deploy,
    state: legacy?.state ?? deploy.state!,
    substate: legacy ? legacy.substate : deploy.substate,
    attention: legacy?.attention ?? deploy.attention!,
  };
});

const PortfolioEnvironmentSchema = z.object({
  environment: z.string(),
  lastDeploy: nullable(PortfolioDeploySchema),
  running: nullable(PortfolioDeploySchema),
  weekly: z.array(z.number().int().nonnegative()),
  lastAttempt: nullable(PortfolioDeploySchema),
  stopped: TargetStopSchema.nullish().catch(null),
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
