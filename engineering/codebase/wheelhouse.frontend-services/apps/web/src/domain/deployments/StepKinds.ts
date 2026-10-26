/** What one rollout step does, read from the name the target runner gives it. */
export const StepKind = {
  RenderSettings: 'render_settings',
  CheckTarget: 'check_target',
  Snapshot: 'snapshot',
  Pull: 'pull',
  Migrations: 'migrations',
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

/** Step names that carry what they act on after a fixed opening word: `Start desk, jobs`, `Roll back to v0.8.4`. @internal */
const PREFIXED: readonly (readonly [string, StepKind])[] = [
  ['Roll back', StepKind.RollBack],
  ['Restore ', StepKind.Restore],
  ['Restart ', StepKind.Restart],
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
} as const;
