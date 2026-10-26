import type { Attention, DeployState } from '@/domain/common/StatusVocabulary';
import type { TargetStop } from '@/domain/deployments/controls/ServiceControls';

/** What a rollout's status amounts to, as the host reports it. */
export const RolloutOutcome = {
  Succeeded: 'succeeded',
  Failed: 'failed',
  Refused: 'refused',
  Pending: 'pending',
  Unknown: 'unknown',
} as const;
export type RolloutOutcome = (typeof RolloutOutcome)[keyof typeof RolloutOutcome];

/** One rollout as an environment pill's details describe it. */
export interface PortfolioDeploy {
  id: string;
  /** The release label, such as `v1.2.0` or `sha-abc1234`. */
  release: string | null;
  /** The runner's status, such as `succeeded` or `rolled_back`. */
  status: string;
  outcome: RolloutOutcome;
  actor: string | null;
  submittedAt: string;
  completedAt: string | null;
  /** Why it failed, display-safe. */
  reason: string | null;
  /** What the environment runs because of it, in the status taxonomy. */
  state: DeployState;
  substate: string | null;
  attention: Attention;
  /** How many of its services run the release it brought; null when the target did not report its services. */
  servicesOnRelease: number | null;
  serviceCount: number | null;
  /** Its watch window's status after a success, such as `watching`. */
  watch: string | null;
}

/** One environment of a product in the portfolio summary. */
export interface PortfolioEnvironment {
  environment: string;
  /** The newest rollout that changed or tried to change the environment, whatever its outcome. */
  lastDeploy: PortfolioDeploy | null;
  /** The newest rollout that succeeded, so the release it serves. */
  running: PortfolioDeploy | null;
  /** Finished rollouts per week, aligned with the summary's weeks. */
  weekly: number[];
  /** A newer deploy that changed nothing but needs a look (a failed check); it marks the pill's corner. */
  lastAttempt: PortfolioDeploy | null;
  /** What the owner keeps stopped there; the pill reads stopped, never failed. */
  stopped?: TargetStop | null;
}

/** One product's deployments over the summary's weeks. */
export interface PortfolioProduct {
  product: string;
  workspace: string | null;
  succeeded: number;
  failed: number;
  environments: PortfolioEnvironment[];
}

/** One week's finished rollouts. */
export interface PortfolioWeek {
  succeeded: number;
  failed: number;
}

/** One workspace's finished rollouts over the summary's weeks. */
export interface PortfolioWorkspace {
  workspace: string;
  succeeded: number;
  failed: number;
  weekly: PortfolioWeek[];
}

/** The Products page's deployment read: per product and environment, and per workspace, over recent weeks. */
export interface PortfolioSummary {
  timeZone: string;
  /** Each week's Monday (`YYYY-MM-DD`), oldest first; the last is the current week. */
  weeks: string[];
  products: PortfolioProduct[];
  workspaces: PortfolioWorkspace[];
}

/** A success rate in percent, whole; null when nothing finished. */
export function successRate(succeeded: number, failed: number): number | null {
  const total = succeeded + failed;
  return total ? Math.round((succeeded / total) * 100) : null;
}
