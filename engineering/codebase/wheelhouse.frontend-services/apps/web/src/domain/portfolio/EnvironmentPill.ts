import { Attention, DeployState, StatusState, StatusWords } from '@/domain/common/StatusVocabulary';
import { DeploymentStates } from '@/domain/deployments/DeploymentStatus';
import { ReleaseSets } from '@/domain/deployments/ReleaseSets';
import { EnvironmentHealth, type EnvironmentReading } from '@/domain/overview/EnvironmentHealth';
import { ProductDeploy } from '@/domain/products/Product';
import { RolloutOutcome, type PortfolioDeploy, type PortfolioEnvironment } from './PortfolioSummary';
import type { TargetStop } from '@/domain/deployments/controls/ServiceControls';

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
  /** Serving, but not as intended: its latest rollout rolled back or regressed, or restarts or host pressure. */
  Degraded: StatusState.Degraded,
  /** Serving a mix of releases: its latest rollout stopped part-way or restored only some services. */
  Mixed: StatusState.Mixed,
  /** No target, or a target with nothing deployed: one state. */
  None: StatusState.None,
  /** Deployed by its own CI rather than by Wheelhouse, as Wheelhouse itself is. */
  Ci: StatusState.Ci,
  /** Stopped by the owner, whole or some services; deploys wait until it starts again. */
  Stopped: StatusState.Stopped,
  /** No reading of the target. */
  Unknown: StatusState.Unknown,
} as const;
export type PillState = (typeof PillState)[keyof typeof PillState];

/** Each state's words, for the pill's assistive label and its details. */
export const PillStateWords: Readonly<Record<PillState, string>> = StatusWords;

/** The cause a degraded pill names when the vitals say no more. @internal */
const StrainCause = 'restarts or host pressure';

/**
 * The one corner marker a pill may wear, by priority: the last attempt changed nothing but needs a look (`attempt`, a
 * cross) › a deploy is scheduled or waiting (`waiting`, a clock) › the serving release is being watched (`watching`, an
 * eye). Refused, cancelled and superseded deploys never mark a pill.
 */
export type PillMarker = 'attempt' | 'waiting' | 'watching';

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
  /** The corner marker, when one applies. */
  readonly marker: PillMarker | null;
  /** The newer deploy that changed nothing but needs a look, which the `attempt` marker points at. */
  readonly lastAttempt: PortfolioDeploy | null;
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
  /** Whether a deploy waits for the target: queued or scheduled. */
  readonly waiting?: boolean;
  /** What the target reports stopped, when the vitals carry it; the summary's own reading wins. */
  readonly stopped?: TargetStop | null;
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

/** A release label as a pill shows it: `v1.2.0` reads `1.2.0`, build metadata after `+` dropped; a release set reads
 * as its first repository's version and how many more (`platform 0.1.8 +2`). */
export function shortRelease(release: string | null | undefined): string | null {
  if (!release) return null;
  if (ReleaseSets.isSet(release)) return ReleaseSets.short(release);
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
    const lastDeploy = withState(input.summary?.lastDeploy ?? null);
    const running = withState(input.summary?.running ?? null);
    const lastAttempt = withState(input.summary?.lastAttempt ?? null);
    const release = running?.release ?? input.reportedRelease ?? null;
    const health = input.reading?.health;
    const hasTarget = health !== undefined && health !== EnvironmentHealth.Missing;
    const marker: PillMarker | null =
      lastAttempt?.attention === Attention.Look
        ? 'attempt'
        : input.waiting
          ? 'waiting'
          : running?.watch === 'watching' && running.id === lastDeploy?.id
            ? 'watching'
            : null;
    const pill = (state: PillState, detail: string | null = PillStateWords[state], cause: string | null = null): EnvironmentPill => ({
      environment: input.environment, state, detail, cause, release, lastDeploy, running, hasTarget, marker, lastAttempt,
    });

    if (input.ci) return { ...pill(PillState.Ci, shortRelease(input.ci.version)), release: input.ci.version, marker: null };
    if (health === EnvironmentHealth.Missing) return pill(PillState.None, null);
    const stopped = input.summary?.stopped ?? input.stopped ?? null;
    if (!stopped && health === EnvironmentHealth.Stopped)
      return pill(PillState.Stopped, PillStateWords[PillState.Stopped], 'stopped by the owner');
    if (stopped && (stopped.environment || stopped.services.length))
      return pill(PillState.Stopped, stopped.environment ? PillStateWords[PillState.Stopped] : `${stopped.services.length} stopped`,
        stopped.environment ? 'stopped by the owner' : `${stopped.services.join(', ')} stopped by the owner`);
    if (lastDeploy?.outcome === RolloutOutcome.Pending || lastDeploy?.state === DeployState.InProgress) return pill(PillState.Deploying);
    if (health === EnvironmentHealth.Failing) return pill(PillState.Failed);
    if (lastDeploy && !supersedes(running, lastDeploy)) {
      if (lastDeploy.state === DeployState.RolledBack)
        return pill(PillState.Degraded, undefined, release ? `rolled back to ${ReleaseSets.text(release)}` : 'rolled back to the previous release');
      if (lastDeploy.state === DeployState.Partial)
        return pill(PillState.Mixed, mixedDetail(lastDeploy), lastDeploy.reason ?? 'the environment runs two releases');
      if (lastDeploy.state === DeployState.Failed) return pill(PillState.Failed, undefined, lastDeploy.reason);
    }
    if (lastDeploy?.state === DeployState.Degraded && lastDeploy.id === running?.id)
      return pill(PillState.Degraded, shortRelease(release), lastDeploy.reason ?? 'regressed after the deploy');
    if (health === EnvironmentHealth.Warning) return pill(PillState.Degraded, undefined, StrainCause);
    if (health === EnvironmentHealth.Idle) return pill(PillState.None, null);
    if (health === EnvironmentHealth.Unknown || health === undefined) return pill(PillState.Unknown);
    return pill(PillState.Healthy, shortRelease(release));
  },
} as const;

/** A rollout with its state, read from its runner status when the read carries none. @internal */
function withState(deploy: PortfolioDeploy | null): PortfolioDeploy | null {
  if (!deploy || (deploy.state && deploy.attention)) return deploy;
  return { ...deploy, ...DeploymentStates.legacy(deploy.status), servicesOnRelease: deploy.servicesOnRelease ?? null,
    serviceCount: deploy.serviceCount ?? null, watch: deploy.watch ?? null };
}

/** A mixed pill's detail: `2/3 on 0.4.0` when the target reported its services, else the word. @internal */
function mixedDetail(deploy: PortfolioDeploy): string {
  return deploy.servicesOnRelease !== null && deploy.serviceCount
    ? `${deploy.servicesOnRelease}/${deploy.serviceCount} on ${shortRelease(deploy.release) ?? 'the new release'}`
    : PillStateWords[PillState.Mixed];
}

/** Whether a success came after the failure, so the failure no longer stands. @internal */
function supersedes(success: PortfolioDeploy | null, failure: PortfolioDeploy): boolean {
  return success !== null && Date.parse(success.submittedAt) > Date.parse(failure.submittedAt);
}
