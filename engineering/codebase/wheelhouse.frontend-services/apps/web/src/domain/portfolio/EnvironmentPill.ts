import { StatusState, StatusWords } from '@/domain/common/StatusVocabulary';
import { EnvironmentHealth, type EnvironmentReading } from '@/domain/overview/EnvironmentHealth';
import { ProductDeploy } from '@/domain/products/Product';
import { RolloutOutcome, type PortfolioDeploy, type PortfolioEnvironment } from './PortfolioSummary';

/**
 * What one environment of a product looks like on its pill: a subset of the console's status vocabulary
 * (`StatusVocabulary`), so the pill's words, tones and icons match every other status. Rolled back is not a state of
 * its own: it is degraded, and the pill's details name the cause.
 */
export const PillState = {
  /** Serving its release, with nothing to look at. Neutral, with only the check icon green. */
  Healthy: StatusState.Healthy,
  /** A rollout is running. */
  Deploying: StatusState.Deploying,
  /** Unreachable, failing, or its latest rollout failed with no success since. */
  Failed: StatusState.Failed,
  /** Serving, but not as intended: its latest rollout rolled back, or restarts or host pressure. */
  Degraded: StatusState.Degraded,
  /** No target, or a target with nothing deployed: one state. */
  None: StatusState.None,
  /** Deployed by its own CI rather than by Wheelhouse, as Wheelhouse itself is. */
  Ci: StatusState.Ci,
  /** No reading of the target. */
  Unknown: StatusState.Unknown,
} as const;
export type PillState = (typeof PillState)[keyof typeof PillState];

/** Each state's words, for the pill's assistive label and its details. */
export const PillStateWords: Readonly<Record<PillState, string>> = StatusWords;

/** The cause a degraded pill names when the vitals say no more. @internal */
const StrainCause = 'restarts or host pressure';

/** One environment's pill: its state, what it shows and what its details say. */
export interface EnvironmentPill {
  readonly environment: string;
  readonly state: PillState;
  /** What follows the environment's name: the serving version for a calm state, the state word otherwise. */
  readonly detail: string | null;
  /** Why the state holds, for its details: `rolled back to v1.2.0`, `restarts or host pressure`; null when calm. */
  readonly cause: string | null;
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

/** What decides whether a product's pills read CI. */
export interface CiProduct {
  /** What deploys the product; undefined while the product list loads. */
  readonly deploy: ProductDeploy | undefined;
  /** How many targets this console has for the product. */
  readonly targets: number;
  /** For this console's own product, the environment it serves and the version it runs; null for any other. */
  readonly console: { readonly environment: string | null; readonly version: string | null } | null;
}

/** A release label as a pill shows it: `v1.2.0` reads `1.2.0`, build metadata after `+` dropped. */
export function shortRelease(release: string | null | undefined): string | null {
  if (!release) return null;
  const plain = release.split('+')[0] ?? release;
  return /^v\d/.test(plain) ? plain.slice(1) : plain;
}

/** Pure rules turning an environment's health and rollouts into its pill. */
export const EnvironmentPillRules = {
  /**
   * The CI reading of one environment's pill: a product its own CI deploys, with no target in this console, reads CI in
   * every environment; only this console's own product shows a version, the one it runs in the environment it serves.
   * Null for a product Wheelhouse or nothing deploys, or one this console has targets for.
   */
  ciOf: (product: CiProduct, environment: string): { readonly version: string | null } | null =>
    product.deploy === ProductDeploy.Ci && product.targets === 0
      ? { version: product.console?.environment === environment ? product.console.version : null }
      : null,
  of: (input: PillInput): EnvironmentPill => {
    const lastDeploy = input.summary?.lastDeploy ?? null;
    const running = input.summary?.running ?? null;
    const release = running?.release ?? input.reportedRelease ?? null;
    const health = input.reading?.health;
    const hasTarget = health !== undefined && health !== EnvironmentHealth.Missing;
    const pill = (state: PillState, detail: string | null = PillStateWords[state], cause: string | null = null): EnvironmentPill => ({
      environment: input.environment, state, detail, cause, release, lastDeploy, running, hasTarget,
    });

    if (input.ci) return { ...pill(PillState.Ci, shortRelease(input.ci.version)), release: input.ci.version };
    if (health === EnvironmentHealth.Missing) return pill(PillState.None, null);
    if (lastDeploy?.outcome === RolloutOutcome.Pending) return pill(PillState.Deploying);
    if (health === EnvironmentHealth.Failing) return pill(PillState.Failed);
    if (lastDeploy?.outcome === RolloutOutcome.Failed && !supersedes(running, lastDeploy))
      return lastDeploy.status === 'rolled_back'
        ? pill(PillState.Degraded, undefined, release ? `rolled back to ${release}` : 'rolled back to the previous release')
        : pill(PillState.Failed, undefined, lastDeploy.reason);
    if (health === EnvironmentHealth.Warning) return pill(PillState.Degraded, undefined, StrainCause);
    if (health === EnvironmentHealth.Idle) return pill(PillState.None, null);
    if (health === EnvironmentHealth.Unknown || health === undefined) return pill(PillState.Unknown);
    return pill(PillState.Healthy, shortRelease(release));
  },
} as const;

/** Whether a success came after the failure, so the failure no longer stands. @internal */
function supersedes(success: PortfolioDeploy | null, failure: PortfolioDeploy): boolean {
  return success !== null && Date.parse(success.submittedAt) > Date.parse(failure.submittedAt);
}
