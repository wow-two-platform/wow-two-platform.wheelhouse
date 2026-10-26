import { TargetCondition, type DeploymentJob, type DeploymentStats, type DeploymentTarget } from '@/domain/deployments';
import { RESTART_WARNING, ServerExtensions, type ServerVitals } from '@/domain/servers';
import { EnvironmentOrder, type DeploymentEnvironment } from '@/domain/targets';
import { ServiceControls } from '@/domain/deployments/controls/ServiceControls';

/** What one environment of a product looks like from the readings the console already holds. */
export const EnvironmentHealth = {
  /** The product has no target for the environment. */
  Missing: 'missing',
  /** No reading yet, or the reading could not say. */
  Unknown: 'unknown',
  /** Reachable, with nothing deployed. */
  Idle: 'idle',
  Healthy: 'healthy',
  /** Serving, with something to look at: a rollout running or a container restarting. */
  Warning: 'warning',
  /** Unreachable, failing, empty after a release, or waiting on reconciliation. */
  Failing: 'failing',
  /** Stopped whole by the owner: on purpose, so never failing. */
  Stopped: 'stopped',
} as const;
export type EnvironmentHealth = (typeof EnvironmentHealth)[keyof typeof EnvironmentHealth];

/** One environment's health on a product card. */
export interface EnvironmentReading {
  environment: DeploymentEnvironment;
  health: EnvironmentHealth;
  /** The target the reading belongs to; null when the product has none for the environment. */
  targetId: string | null;
}

/** The readings an environment's health comes from; a read still loading is undefined. */
export interface EnvironmentReadings {
  readonly vitals: ServerVitals | undefined;
  readonly stats?: DeploymentStats | undefined;
}

/** Each health's words for a dot's label. */
export const EnvironmentHealthLabels: Readonly<Record<EnvironmentHealth, string>> = {
  missing: 'not set up',
  unknown: 'no reading',
  idle: 'nothing deployed',
  healthy: 'healthy',
  warning: 'needs a look',
  failing: 'failing',
  stopped: 'stopped',
};

/** Pure rules turning targets, vitals and deployment stats into per-environment health. */
export const EnvironmentHealthRules = {
  /** One target's health from the latest vitals and stats. */
  ofTarget: (target: DeploymentTarget, readings: EnvironmentReadings): EnvironmentHealth => {
    if (!readings.vitals) return EnvironmentHealth.Unknown;
    const reading = readings.vitals.targets.find((item) => item.targetId === target.id);
    if (!reading) return EnvironmentHealth.Unknown;
    if (!reading.ok) return EnvironmentHealth.Failing;
    if (reading.condition === TargetCondition.NeedsReconciliation) return EnvironmentHealth.Failing;
    if (readings.stats?.targets.some((item) => item.targetId === target.id && item.failingSince !== null))
      return EnvironmentHealth.Failing;
    if (reading.stopped?.environment) return EnvironmentHealth.Stopped;
    const containers = reading.containers;
    if (containers == null) return EnvironmentHealth.Unknown;
    if (containers.length === 0) return reading.release ? EnvironmentHealth.Failing : EnvironmentHealth.Idle;
    // A service the owner stopped is stopped on purpose, not failing.
    if (containers.some((container) => !ServerExtensions.isHealthy(container)
      && !ServiceControls.isStopped(reading.stopped, container.service))) return EnvironmentHealth.Failing;
    if (reading.condition === TargetCondition.Running) return EnvironmentHealth.Warning;
    if (containers.some((container) => container.restarts >= RESTART_WARNING)) return EnvironmentHealth.Warning;
    return EnvironmentHealth.Healthy;
  },

  /**
   * Dev and prod for one product, in promotion order. An environment with several targets shows the worst of
   * them, so a failing one is never hidden behind a healthy one.
   */
  ofProduct: (
    product: string,
    targets: readonly DeploymentTarget[],
    readings: EnvironmentReadings,
  ): EnvironmentReading[] =>
    EnvironmentOrder.map((environment) => {
      const matching = targets.filter((target) => target.product === product && target.environment === environment);
      if (!matching.length) return { environment, health: EnvironmentHealth.Missing, targetId: null };
      const healths = matching.map((target) => EnvironmentHealthRules.ofTarget(target, readings));
      const worst = healths.reduce((left, right) => (Severity[right] > Severity[left] ? right : left));
      return { environment, health: worst, targetId: matching[healths.indexOf(worst)]?.id ?? null };
    }),

  /** The newest submission to any of the targets; history arrives newest first. */
  lastDeploy: (targetIds: readonly string[], history: readonly DeploymentJob[] | undefined): DeploymentJob | null => {
    if (!history || !targetIds.length) return null;
    const ids = new Set(targetIds);
    return history.find((job) => job.targetId != null && ids.has(job.targetId)) ?? null;
  },
} as const;

/** Orders healths from calm to urgent, for "the worst of several". @internal */
const Severity: Readonly<Record<EnvironmentHealth, number>> = {
  missing: 0,
  idle: 1,
  stopped: 1,
  healthy: 2,
  unknown: 3,
  warning: 4,
  failing: 5,
};
