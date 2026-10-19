import { StatusState, StatusWords } from '@/domain/common/StatusVocabulary';

import { JobStatus } from './enums/JobStatus';
import type { DeploymentWatch } from './history/models/DeploymentItem';

/** A deployment's status in the console's vocabulary: its state, the state's word, and why it holds. */
export interface DeploymentStatusReading {
  readonly state: StatusState;
  readonly word: string;
  /** Why the state holds — `rolled back to the previous release`, a failure's reason; null when there is nothing to add. */
  readonly cause: string | null;
}

/** What a deployment's status is read from. */
export interface DeploymentStatusInput {
  readonly status: JobStatus;
  /** The display-safe explanation of its outcome. */
  readonly reason?: string | null | undefined;
  /** The watch after a successful rollout: a regression turns the success degraded. */
  readonly watch?: Pick<DeploymentWatch, 'status' | 'findings'> | null | undefined;
  /** The release a roll back restored, when the caller knows it. */
  readonly restored?: string | null | undefined;
}

/** How many of a set of deployments are in one state. */
export interface DeploymentStatusCount {
  readonly state: StatusState;
  readonly word: string;
  readonly count: number;
}

/** The order counts read in: outcomes first, from calm to urgent, then what is still running or changed nothing. */
export const DeploymentStatusOrder: readonly StatusState[] = [
  StatusState.Succeeded,
  StatusState.Failed,
  StatusState.Degraded,
  StatusState.Interrupted,
  StatusState.Deploying,
  StatusState.Refused,
  StatusState.Unknown,
];

const PENDING: ReadonlySet<JobStatus> = new Set([JobStatus.Submitting, JobStatus.Queued, JobStatus.Running]);

/** Joins a state's own cause with the runner's reason. @internal */
function joined(cause: string, reason: string | null | undefined): string {
  return reason ? `${cause} · ${reason}` : cause;
}

/** Pure rules putting a deployment's runner status into the console's status vocabulary. */
export const DeploymentStatusRules = {
  /**
   * A deployment's state: a success that regressed while watched, and a roll back, are degraded and name the cause; a
   * failure, a failed roll back and an interruption are red; a refusal changed nothing; a live one is deploying.
   */
  of(input: DeploymentStatusInput): DeploymentStatusReading {
    const reading = (state: StatusState, cause: string | null = null): DeploymentStatusReading => ({
      state,
      word: StatusWords[state],
      cause: cause || null,
    });
    const reason = input.reason ?? null;
    switch (input.status) {
      case JobStatus.Succeeded: {
        if (input.watch?.status !== 'degraded') return reading(StatusState.Succeeded);
        const finding = input.watch.findings[0];
        return reading(StatusState.Degraded, finding ? `regressed after the deploy: ${finding}` : 'regressed after the deploy');
      }
      case JobStatus.RolledBack:
        return reading(
          StatusState.Degraded,
          joined(input.restored ? `rolled back to ${input.restored}` : 'rolled back to the previous release', reason),
        );
      case JobStatus.RollbackFailed:
        return reading(StatusState.Failed, joined('restoring the previous release failed too', reason));
      case JobStatus.Failed:
        return reading(StatusState.Failed, reason);
      case JobStatus.Interrupted:
        return reading(StatusState.Interrupted, reason ?? 'stopped mid-way');
      case JobStatus.Rejected:
        return reading(StatusState.Refused, reason);
      default:
        return PENDING.has(input.status) ? reading(StatusState.Deploying) : reading(StatusState.Unknown, reason);
    }
  },

  /** How many deployments are in each state, in {@link DeploymentStatusOrder}, leaving out the states none is in. */
  counts(inputs: readonly DeploymentStatusInput[]): DeploymentStatusCount[] {
    const tally = new Map<StatusState, number>();
    for (const input of inputs) {
      const { state } = DeploymentStatusRules.of(input);
      tally.set(state, (tally.get(state) ?? 0) + 1);
    }
    return DeploymentStatusOrder.flatMap((state) => {
      const count = tally.get(state) ?? 0;
      return count ? [{ state, word: StatusWords[state], count }] : [];
    });
  },
} as const;
