import { StatusState } from '@/domain/common/StatusVocabulary';

/** A release's end-to-end check after its dev rollout, as the runner keeps it on the job. */
export interface DeploymentVerification {
  /** `failed` covers a non-zero exit, the timeout and a check that could not start. */
  readonly status: 'passed' | 'failed';
  /** The service whose release image ran the check. */
  readonly service: string | null;
  readonly completedAt: string | null;
  readonly seconds: number | null;
  /** Null when it ran past its timeout, -1 when it could not run. */
  readonly exitCode: number | null;
  /** The operator-safe outcome: `Passed`, `Exited 3`, `Timed out after 300s`. */
  readonly detail: string | null;
  /** The last line the check printed. */
  readonly summary: string | null;
}

/** Where a release's dev check stands. */
export const DevCheckState = {
  Passed: 'passed',
  Failed: 'failed',
  /** The release declares a check that has no result on dev yet. */
  NotRun: 'not_run',
  /** The release declares none, or its bundle does not say. */
  NotDeclared: 'not_declared',
} as const;
export type DevCheckState = (typeof DevCheckState)[keyof typeof DevCheckState];

/** Each check state's word. */
export const DevCheckWords: Readonly<Record<DevCheckState, string>> = {
  passed: 'passed',
  failed: 'failed',
  not_run: 'not run',
  not_declared: 'no check declared',
};

/** A dev check as a pill reads it. */
export interface DevCheckReading {
  readonly state: DevCheckState;
  /** The status vocabulary state that draws the pill: passed calm, failed red, not run dashed, none quiet. */
  readonly status: StatusState;
  readonly word: string;
  /** What the check said, or why it has none; null when there is nothing to add. */
  readonly cause: string | null;
  /** Auto-promote takes a release only once its check passed, or when it declares none. */
  readonly holdsAutoPromote: boolean;
}

const Statuses: Readonly<Record<DevCheckState, StatusState>> = {
  passed: StatusState.Succeeded,
  failed: StatusState.Failed,
  not_run: StatusState.None,
  not_declared: StatusState.Unknown,
};

/** Pure rules reading a promotion's dev check, the same way the host's auto-promote gate does. */
export const DevCheckRules = {
  of: (input: { readonly verification: DeploymentVerification | null; readonly verifies: boolean | null }): DevCheckReading => {
    const check = input.verification;
    const state = check
      ? check.status === 'passed'
        ? DevCheckState.Passed
        : DevCheckState.Failed
      : input.verifies === true
        ? DevCheckState.NotRun
        : DevCheckState.NotDeclared;
    const ran = check
      ? [check.detail, check.service ? `in ${check.service}` : null, check.seconds !== null ? `${check.seconds}s` : null]
          .filter(Boolean)
          .join(' · ')
      : '';
    const cause =
      state === DevCheckState.Failed
        ? [ran, check?.summary].filter(Boolean).join(': ') || null
        : state === DevCheckState.Passed
          ? ran || null
          : state === DevCheckState.NotRun
            ? 'The release declares an end-to-end check that has not run on dev.'
            : input.verifies === null
              ? 'Its bundle does not say whether it declares a check.'
              : null;
    return {
      state,
      status: Statuses[state],
      word: DevCheckWords[state],
      cause,
      holdsAutoPromote: state === DevCheckState.Failed || state === DevCheckState.NotRun,
    };
  },
} as const;
