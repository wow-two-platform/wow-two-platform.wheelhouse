import {
  Attention,
  AttentionTones,
  DeployState,
  DeployStateWords,
  ServiceResult,
  ServiceResultWords,
  SubstateWords,
  type StatusTone,
} from '@/domain/common/StatusVocabulary';

import { JobStatus } from './enums/JobStatus';
import { ReleaseSets } from './ReleaseSets';
import type { DeploymentStep } from './models/DeploymentStep';
import type {
  DeploymentStateCause,
  DeploymentEffect,
  DeploymentServiceResult,
  DeploymentWatch,
} from './history/models/DeploymentItem';

/** A deployment in the status taxonomy, ready for its chip: what it amounts to, which way, how loud and why. */
export interface DeploymentStateReading {
  readonly state: DeployState;
  /** The state's word: `Partial`. */
  readonly word: string;
  readonly substate: string | null;
  /** The sub-state's words: `partly rolled back`; null when there is none. */
  readonly detail: string | null;
  readonly attention: Attention;
  /** The colour, from the attention. */
  readonly tone: StatusTone;
  /** Not applied wears a dashed outline, Unknown a dotted one, so the chip reads in greyscale too. */
  readonly outline: 'solid' | 'dashed' | 'dotted';
  readonly cause: DeploymentStateCause | null;
}

/** What a deployment's reading is made of: the API's state, or a runner status alone where only that is known. */
export interface DeploymentStateInput {
  readonly status: JobStatus | string;
  readonly state?: DeployState | null | undefined;
  readonly substate?: string | null | undefined;
  readonly attention?: Attention | null | undefined;
  readonly cause?: DeploymentStateCause | null | undefined;
  /** The display-safe reason, the cause's message when the API names none. */
  readonly reason?: string | null | undefined;
  readonly watch?: Pick<DeploymentWatch, 'status' | 'findings'> | null | undefined;
}

/** How many of a set of deployments are in one state. */
export interface DeploymentStateCount {
  readonly state: DeployState;
  readonly word: string;
  readonly attention: Attention;
  readonly count: number;
}

/** One line of a deployment's impact: an origin, or an effect indented under the origin it likely came from. */
export interface ImpactLine {
  readonly service: string;
  readonly result: ServiceResult;
  /** `origin`, `effect` (likely via an origin), or `other` (hurt, with no declared path to an origin). */
  readonly role: 'origin' | 'effect' | 'other';
  readonly via: string | null;
  readonly text: string;
}

/** A rollout's steps, cut where changes start: cancel is refused after the boundary. */
export interface StepSections {
  readonly before: readonly DeploymentStep[];
  readonly changes: readonly DeploymentStep[];
  readonly after: readonly DeploymentStep[];
}

/** The action that fits a state best. */
export type StateAction = 'reconcile' | 'redeploy' | 'retry' | null;

/** The order counts read in: calm to urgent among those that changed the environment, then the rest. */
export const DeploymentStateOrder: readonly DeployState[] = [
  DeployState.Succeeded,
  DeployState.Degraded,
  DeployState.RolledBack,
  DeployState.Partial,
  DeployState.Failed,
  DeployState.InProgress,
  DeployState.NotApplied,
  DeployState.Unknown,
];

const PENDING: ReadonlySet<string> = new Set([JobStatus.Submitting, JobStatus.Queued, JobStatus.Running]);
const STATES: ReadonlySet<string> = new Set(Object.values(DeployState));
const ATTENTIONS: ReadonlySet<string> = new Set(Object.values(Attention));
/** The services a deployment put on its new release, or left there. @internal */
const ON_RELEASE: ReadonlySet<ServiceResult> = new Set([ServiceResult.Updated, ServiceResult.New, ServiceResult.Unchanged]);
/** The steps before a rollout changes anything. @internal */
const BEFORE_CHANGES: ReadonlySet<string> = new Set([
  'Render settings',
  'Check target',
  'Snapshot data',
  'Pull images',
  'Dry-run migrations',
]);
/** The steps once the services run, whatever happened to them. @internal */
const AFTER_CHANGES: ReadonlySet<string> = new Set([
  'Publish sites',
  'Probe sites',
  'Verify end to end',
  'Remove unused images',
  'Remove old files',
]);

/** Capitalizes the first letter. @internal */
const sentence = (text: string) => (text ? text.charAt(0).toUpperCase() + text.slice(1) : text);

/** Pure rules putting a deployment into the console's status taxonomy. */
export const DeploymentStates = {
  /**
   * The state, sub-state and attention of a runner status alone, for reads that carry no state (a runner job, an older
   * answer): a roll back is Rolled back, a failed roll back and an interruption Failed, a refusal Not applied.
   */
  legacy(status: string, watch?: Pick<DeploymentWatch, 'status'> | null): {
    state: DeployState;
    substate: string | null;
    attention: Attention;
  } {
    switch (status) {
      case JobStatus.Succeeded:
        return watch?.status === 'degraded'
          ? { state: DeployState.Degraded, substate: 'in_watch', attention: Attention.Look }
          : { state: DeployState.Succeeded, substate: watch?.status ?? null, attention: Attention.None };
      case JobStatus.RolledBack:
        return { state: DeployState.RolledBack, substate: 'automatic', attention: Attention.Look };
      case JobStatus.RollbackFailed:
        return { state: DeployState.Failed, substate: 'rollback_failed', attention: Attention.Act };
      case JobStatus.Failed:
        return { state: DeployState.Failed, substate: null, attention: Attention.Act };
      case JobStatus.Interrupted:
        return { state: DeployState.Failed, substate: 'interrupted', attention: Attention.Act };
      case JobStatus.Rejected:
        return { state: DeployState.NotApplied, substate: 'refused', attention: Attention.None };
      default:
        return PENDING.has(status)
          ? { state: DeployState.InProgress, substate: status === JobStatus.Running ? null : 'queued', attention: Attention.Info }
          : { state: DeployState.Unknown, substate: 'no_receipt', attention: Attention.Muted };
    }
  },

  /** A deployment's reading: the API's state when it carries one, else its runner status read the old way. */
  of(input: DeploymentStateInput): DeploymentStateReading {
    const known = input.state && STATES.has(input.state) && input.attention && ATTENTIONS.has(input.attention);
    const base = known
      ? { state: input.state!, substate: input.substate ?? null, attention: input.attention! }
      : DeploymentStates.legacy(input.status, input.watch);
    const cause =
      input.cause ??
      (input.reason ? { kind: 'step', origin: null, message: input.reason, at: null } : null);
    return {
      ...base,
      word: DeployStateWords[base.state],
      detail: base.substate ? (SubstateWords[base.substate] ?? base.substate.replaceAll('_', ' ')) : null,
      tone: AttentionTones[base.attention],
      outline: base.state === DeployState.NotApplied ? 'dashed' : base.state === DeployState.Unknown ? 'dotted' : 'solid',
      cause: base.attention === Attention.None && base.state === DeployState.Succeeded ? null : cause,
    };
  },

  /** The chip's accessible text: `Partial, partly rolled back: jobs did not become healthy`. */
  label(reading: DeploymentStateReading): string {
    const head = reading.detail ? `${reading.word}, ${reading.detail}` : reading.word;
    return reading.cause?.message ? `${head}: ${reading.cause.message}` : head;
  },

  /** How many deployments are in each state, in {@link DeploymentStateOrder}, leaving out the states none is in. */
  counts(inputs: readonly DeploymentStateInput[]): DeploymentStateCount[] {
    const tally = new Map<DeployState, { count: number; attention: Attention }>();
    for (const input of inputs) {
      const reading = DeploymentStates.of(input);
      const held = tally.get(reading.state);
      // A state's tile takes the loudest attention among its deployments.
      const louder = held && Rank[held.attention] >= Rank[reading.attention] ? held.attention : reading.attention;
      tally.set(reading.state, { count: (held?.count ?? 0) + 1, attention: louder });
    }
    return DeploymentStateOrder.flatMap((state) => {
      const held = tally.get(state);
      return held ? [{ state, word: DeployStateWords[state], attention: held.attention, count: held.count }] : [];
    });
  },

  /** The words a service strip says on a phone: `2 of 3 updated`, `1 of 4 changed`, `3 restored`. */
  stripSummary(services: readonly DeploymentServiceResult[] | null | undefined): string | null {
    if (!services?.length) return null;
    const total = services.length;
    const count = (...results: ServiceResult[]) => services.filter((item) => results.includes(item.result)).length;
    const failed = count(ServiceResult.Failed);
    if (failed) return `${failed} of ${total} failed`;
    const restored = count(ServiceResult.Restored);
    if (restored) return `${restored} of ${total} restored`;
    const live = count(ServiceResult.Waiting, ServiceResult.Starting);
    if (live) return `${total - live} of ${total} done`;
    const changed = count(ServiceResult.Updated, ServiceResult.New, ServiceResult.Affected);
    return changed === total ? `${total} updated` : `${changed} of ${total} updated`;
  },

  /** One service's glyph label: `jobs: failed, where it started`. */
  serviceLabel(service: DeploymentServiceResult): string {
    return `${service.service}: ${ServiceResultWords[service.result]}${service.origin ? ', where it started' : ''}`;
  },

  /**
   * What the environment runs because of a deployment, in one sentence: `prod now runs two releases`, `prod runs
   * v0.9.0`, `prod runs v0.8.4 again`, `prod is unchanged`.
   */
  nowRuns(
    reading: Pick<DeploymentStateReading, 'state'>,
    environment: string | null,
    release: string | null,
    previous: string | null,
  ): string {
    const where = environment ?? 'The target';
    // A release set reads as each repository's release, as everywhere a person reads a label.
    const next = ReleaseSets.text(release) || 'the new release';
    switch (reading.state) {
      case DeployState.InProgress:
        return `${where} is changing to ${next}`;
      case DeployState.Succeeded:
        return `${where} runs ${next}`;
      case DeployState.Degraded:
        return `${where} runs ${next}, not all of it healthy`;
      case DeployState.RolledBack:
        return `${where} runs ${ReleaseSets.text(previous) || 'the previous release'} again`;
      case DeployState.Partial:
        return `${where} now runs two releases`;
      case DeployState.Failed:
        return `${where} is broken or in an unknown state`;
      case DeployState.NotApplied:
        return `${where} is unchanged`;
      default:
        return `${where}: no receipt from the target`;
    }
  },

  /**
   * A deployment's impact as a list: each origin, then the services it likely hurt indented under it (an effect only
   * along a declared path), then any other service that is not fine. Empty when every service is fine.
   */
  impact(services: readonly DeploymentServiceResult[] | null | undefined, effects: readonly DeploymentEffect[]): ImpactLine[] {
    if (!services?.length) return [];
    const byName = new Map(services.map((item) => [item.service, item] as const));
    const lines: ImpactLine[] = [];
    const listed = new Set<string>();
    for (const origin of services.filter((item) => item.origin)) {
      lines.push({ service: origin.service, result: origin.result, role: 'origin', via: null, text: ServiceResultWords[origin.result] });
      listed.add(origin.service);
      for (const effect of effects.filter((item) => item.via === origin.service)) {
        lines.push({
          service: effect.service,
          result: byName.get(effect.service)?.result ?? ServiceResult.Affected,
          role: 'effect',
          via: origin.service,
          text: `${effect.finding}, likely via ${origin.service}`,
        });
        listed.add(effect.service);
      }
    }
    for (const item of services) {
      if (listed.has(item.service)) continue;
      if (item.result === ServiceResult.Failed || item.result === ServiceResult.Affected || item.result === ServiceResult.NotReached || item.result === ServiceResult.Restored)
        lines.push({ service: item.service, result: item.result, role: 'other', via: null, text: ServiceResultWords[item.result] });
    }
    return lines;
  },

  /** How many services run the release a deployment brought, as a pill's detail: `2/3 on 0.4.0`. */
  onRelease(services: readonly DeploymentServiceResult[] | null | undefined): { on: number; total: number } | null {
    if (!services?.length) return null;
    return { on: services.filter((item) => ON_RELEASE.has(item.result)).length, total: services.length };
  },

  /**
   * A rollout's steps cut where changes start: the checks before (snapshot, pull, dry run, drain), the changes from the
   * first step that touches the services, and what follows once they run (sites, the dev check, housekeeping). A
   * rollout that stopped before changes has none after the boundary.
   */
  sections(steps: readonly DeploymentStep[]): StepSections {
    const start = steps.findIndex((step) => !BEFORE_CHANGES.has(step.name) && !step.name.startsWith('Drain') && !step.name.startsWith('Wait'));
    if (start < 0) return { before: steps, changes: [], after: [] };
    const tail = steps.slice(start);
    const end = tail.findIndex((step) => AFTER_CHANGES.has(step.name));
    return end < 0
      ? { before: steps.slice(0, start), changes: tail, after: [] }
      : { before: steps.slice(0, start), changes: tail.slice(0, end), after: tail.slice(end) };
  },

  /** The action that fits a state: Partial and Failed reconcile, Rolled back redeploys, Not applied retries. */
  action(state: DeployState): StateAction {
    return state === DeployState.Partial || state === DeployState.Failed
      ? 'reconcile'
      : state === DeployState.RolledBack
        ? 'redeploy'
        : state === DeployState.NotApplied
          ? 'retry'
          : null;
  },

  /** A cause's message as a sentence. */
  causeText(cause: DeploymentStateCause | null): string | null {
    return cause?.message ? sentence(cause.message) : null;
  },
} as const;

/** Each attention's loudness, for the loudest of several. @internal */
const Rank: Readonly<Record<Attention, number>> = { none: 0, muted: 0, info: 1, look: 2, act: 3 };
