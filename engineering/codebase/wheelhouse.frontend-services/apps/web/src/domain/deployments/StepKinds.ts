import { StepStatus } from './enums/StepStatus';
import type { DeploymentStep } from './models/DeploymentStep';

/** What one rollout step does, read from the name the target runner gives it. */
export const StepKind = {
  RenderSettings: 'render_settings',
  CheckTarget: 'check_target',
  Snapshot: 'snapshot',
  Pull: 'pull',
  /** Migrations: the dry run before changes, and a service's migrate step (`Migrate api`). */
  Migrations: 'migrations',
  /** Whether a release may run on the database as it stands (`Rollback gate api`), before a roll back or a deploy of
   * an older release. */
  RollbackGate: 'rollback_gate',
  Drain: 'drain',
  Wait: 'wait',
  Start: 'start',
  Stop: 'stop',
  Restart: 'restart',
  Verify: 'verify',
  PublishSites: 'publish_sites',
  ProbeSites: 'probe_sites',
  VerifyEndToEnd: 'verify_end_to_end',
  RemoveImages: 'remove_images',
  RemoveFiles: 'remove_files',
  RollBack: 'roll_back',
  Restore: 'restore',
  /** A step this console does not know yet; it still shows, with a neutral mark. */
  Other: 'other',
} as const;
export type StepKind = (typeof StepKind)[keyof typeof StepKind];

/** The runner's fixed step names. @internal */
const NAMED: Readonly<Record<string, StepKind>> = {
  'Render settings': StepKind.RenderSettings,
  'Check target': StepKind.CheckTarget,
  'Snapshot data': StepKind.Snapshot,
  'Pull images': StepKind.Pull,
  'Dry-run migrations': StepKind.Migrations,
  'Start containers': StepKind.Start,
  'Verify services': StepKind.Verify,
  'Publish sites': StepKind.PublishSites,
  'Probe sites': StepKind.ProbeSites,
  'Verify end to end': StepKind.VerifyEndToEnd,
  'Remove unused images': StepKind.RemoveImages,
  'Remove old files': StepKind.RemoveFiles,
};

/** Step names that carry what they act on after a fixed opening word: `Start desk, jobs`, `Roll back to v0.8.4`. A
 * longer opening comes before a shorter one it starts with. @internal */
const PREFIXED: readonly (readonly [string, StepKind])[] = [
  ['Rollback gate', StepKind.RollbackGate],
  ['Roll back', StepKind.RollBack],
  ['Restore ', StepKind.Restore],
  ['Restart ', StepKind.Restart],
  ['Migrate ', StepKind.Migrations],
  ['Start ', StepKind.Start],
  ['Stop ', StepKind.Stop],
  ['Drain', StepKind.Drain],
  ['Wait', StepKind.Wait],
];

/** Pure rules over rollout steps' kinds. */
export const StepKinds = {
  /** The kind of a step by its name; a name the runner adds later reads as `other`. */
  of(name: string): StepKind {
    const named = NAMED[name];
    if (named) return named;
    return PREFIXED.find(([prefix]) => name.startsWith(prefix))?.[1] ?? StepKind.Other;
  },

  /**
   * The steps of an automatic restore, drawn nested under the failure they answer: each roll back and restore, and a
   * rollback gate that follows a failed step. A gate before any failure guards a deploy of an older release and stays
   * a step of its own.
   */
  restoreSteps(steps: readonly DeploymentStep[]): ReadonlySet<DeploymentStep> {
    const restore = new Set<DeploymentStep>();
    let failed = false;
    for (const step of steps) {
      const kind = StepKinds.of(step.name);
      if (kind === StepKind.RollBack || kind === StepKind.Restore || (kind === StepKind.RollbackGate && failed))
        restore.add(step);
      if (step.status === StepStatus.Failed) failed = true;
    }
    return restore;
  },
} as const;
