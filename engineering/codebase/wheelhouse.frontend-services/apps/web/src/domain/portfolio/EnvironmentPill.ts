import { EnvironmentHealth, type EnvironmentReading } from '@/domain/overview/EnvironmentHealth';
import { RolloutOutcome, type PortfolioDeploy, type PortfolioEnvironment } from './PortfolioSummary';

/** What one environment of a product looks like on its pill. Every state differs by icon; colour is never alone. */
export const PillState = {
  /** Serving its release, with nothing to look at. Neutral, with only the check icon green. */
  Healthy: 'healthy',
  /** A rollout is running. */
  Deploying: 'deploying',
  /** Unreachable, failing, or its latest rollout failed with no success since. */
  Failed: 'failed',
  /** Its latest rollout rolled back, so the previous release serves. */
  RolledBack: 'rolled-back',
  /** Serving, with something to look at: restarts or host pressure. */
  Degraded: 'degraded',
  /** No target, or a target with nothing deployed: one state. */
  None: 'none',
  /** Deployed by its own CI rather than by Wheelhouse, as Wheelhouse itself is. */
  Ci: 'ci',
  /** No reading of the target. */
  Unknown: 'unknown',
} as const;
export type PillState = (typeof PillState)[keyof typeof PillState];

/** Each state's words, for the pill's assistive label and its details. */
export const PillStateWords: Readonly<Record<PillState, string>> = {
  healthy: 'healthy',
  deploying: 'deploying',
  failed: 'failed',
  'rolled-back': 'rolled back',
  degraded: 'degraded',
  none: 'not deployed',
  ci: 'deployed by CI',
  unknown: 'no reading',
};

/** One environment's pill: its state, what it shows and what its details say. */
export interface EnvironmentPill {
  readonly environment: string;
  readonly state: PillState;
  /** What follows the environment's name: the serving version for a calm state, the state word otherwise. */
  readonly detail: string | null;
  /** The release serving, in full; null when none is known. */
  readonly release: string | null;
  /** The newest rollout the runner accepted. */
  readonly lastDeploy: PortfolioDeploy | null;
  /** The newest rollout that succeeded. */
  readonly running: PortfolioDeploy | null;
  /** Whether the product has a target in the environment; "not deployed" covers both cases on the pill. */
  readonly hasTarget: boolean;
}

/** What a pill is read from; a read still loading is undefined. */
export interface PillInput {
  readonly environment: string;
  /** The environment's health from the host vitals, read without deployment stats: the summary carries rollouts. */
  readonly reading: EnvironmentReading | undefined;
  readonly summary: PortfolioEnvironment | undefined;
  /** Set when the product's own CI deploys it: the version this console runs when it serves the environment. */
  readonly ci?: { readonly version: string | null } | null;
  /** The release the target reports running, when the vitals carry one. */
  readonly reportedRelease?: string | null;
}

/** A release label as a pill shows it: `v1.2.0` reads `1.2.0`, build metadata after `+` dropped. */
export function shortRelease(release: string | null | undefined): string | null {
  if (!release) return null;
  const plain = release.split('+')[0] ?? release;
  return /^v\d/.test(plain) ? plain.slice(1) : plain;
}

/** Pure rules turning an environment's health and rollouts into its pill. */
export const EnvironmentPillRules = {
  of: (input: PillInput): EnvironmentPill => {
    const lastDeploy = input.summary?.lastDeploy ?? null;
    const running = input.summary?.running ?? null;
    const release = running?.release ?? input.reportedRelease ?? null;
    const health = input.reading?.health;
    const hasTarget = health !== undefined && health !== EnvironmentHealth.Missing;
    const pill = (state: PillState, detail: string | null = PillStateWords[state]): EnvironmentPill => ({
      environment: input.environment, state, detail, release, lastDeploy, running, hasTarget,
    });

    if (input.ci) return { ...pill(PillState.Ci, shortRelease(input.ci.version)), release: input.ci.version };
    if (health === EnvironmentHealth.Missing) return pill(PillState.None, null);
    if (lastDeploy?.outcome === RolloutOutcome.Pending) return pill(PillState.Deploying);
    if (health === EnvironmentHealth.Failing) return pill(PillState.Failed);
    if (lastDeploy?.outcome === RolloutOutcome.Failed && !supersedes(running, lastDeploy))
      return pill(lastDeploy.status === 'rolled_back' ? PillState.RolledBack : PillState.Failed);
    if (health === EnvironmentHealth.Warning) return pill(PillState.Degraded);
    if (health === EnvironmentHealth.Idle) return pill(PillState.None, null);
    if (health === EnvironmentHealth.Unknown || health === undefined) return pill(PillState.Unknown);
    return pill(PillState.Healthy, shortRelease(release));
  },
} as const;

/** Whether a success came after the failure, so the failure no longer stands. @internal */
function supersedes(success: PortfolioDeploy | null, failure: PortfolioDeploy): boolean {
  return success !== null && Date.parse(success.submittedAt) > Date.parse(failure.submittedAt);
}
