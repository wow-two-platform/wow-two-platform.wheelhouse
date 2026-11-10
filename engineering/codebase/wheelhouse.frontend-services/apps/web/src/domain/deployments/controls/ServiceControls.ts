import type { RunInput } from "./RunFiles";
/**
 * Service controls (decision 45): the owner stops, starts or restarts the release an environment runs — one service, or
 * the whole environment for a stop or a start. A stop keeps containers, volumes and routes; every deploy to an
 * environment with something stopped is refused until a start, so an automatic deploy never starts it silently. A reset
 * (v0.9) snapshots the environment's database, drops and creates it again empty, and leaves every service stopped for
 * the next deploy, whose migrate step builds the schema and starts them. A set reset rolls one migration set back to 0
 * with the running release's own scripts instead, and stops only the service hosting it; the next deploy builds the
 * set again and starts it. A run executes one command once in a one-off container of a service's running release, with
 * the service's settings and database network; every service keeps running and nothing stopped changes. Its job keeps
 * the command as a masked label, the exit code and the last output line, never the arguments.
 */

import { Measures } from '@/domain/common/Measures';

/** What a control does. */
export const ControlOperation = {
  Stop: 'stop',
  Start: 'start',
  Restart: 'restart',
  Reset: 'reset',
  Run: 'run',
  /** Moves a dev target's legacy files into Files: a plan, a push or a verify. */
  FilesMigration: 'files-migration',
} as const;
export type ControlOperation = (typeof ControlOperation)[keyof typeof ControlOperation];

/** A control as the history records it: its kind and the service it named, none for the whole environment; a set
 * reset also names its migration set, and a run its command as a masked label (`dotnet Identity.Api.dll admin create
 * --slug …`) — the arguments never come back. */
export interface DeploymentOperation {
  kind: ControlOperation;
  services: string[];
  set?: string | null | undefined;
  command?: string | null | undefined;
  /** A files migration's step: `plan`, `push` or `verify`. */
  step?: string | null | undefined;
  /** A files migration's source ids. */
  sources?: string[] | null | undefined;
}

/** How a run's command ended: it exited 0, it exited otherwise or never started, or it outran its time limit. */
export type ControlRunStatus = 'succeeded' | 'failed' | 'timed out';

/** What a run kept on its job: the service it ran in, its command as a masked label, how it ended, its exit code (none
 * when it never exited), how many seconds it took and its last output line, already scrubbed by the target. When the
 * scrub cut something from that line (an invitation link), `reveal` says until when the line as printed can be read,
 * once. */
export interface ControlRun {
  service: string;
  command: string;
  status: ControlRunStatus;
  exitCode: number | null;
  seconds: number;
  lastLine: string | null;
  reveal?: { expiresAt: string } | null;
  /** The lines it printed before its last, scrubbed the same way, oldest first. */
  tail?: string[] | null;
  /** The file it took on its stdin: its size and SHA-256. */
  input?: RunInput | null;
}

/** A control the target queued: its submission, which the history and outcome reads follow. */
export interface QueuedControl {
  id: string;
  targetId: string;
  operation: DeploymentOperation;
  release?: string | null | undefined;
  status: string;
}

/** What the owner keeps stopped on an environment: the whole of it, or the services named. */
export interface TargetStop {
  environment: boolean;
  services: string[];
  at?: string | null;
  actor?: string | null;
}

/** The environment a control acts on, as the console knows it. */
export interface ControlTarget {
  readonly id: string;
  readonly environment: string;
  readonly product: string;
}

/** The migration set a reset rolls back alone, and the service whose image hosts it. */
export interface ControlSet {
  readonly name: string;
  readonly service: string;
}

/** One control the owner asks for: what, on which environment, and which service (none for the environment); a reset
 * of one migration set names it, and a run carries the arguments of its command, already split. */
export interface ControlRequest {
  readonly target: ControlTarget;
  readonly operation: ControlOperation;
  readonly service: string | null;
  readonly set?: ControlSet | null;
  readonly command?: readonly string[] | null;
  /** A file for a run's stdin, base64. */
  readonly input?: string | null;
}

/** Pure rules for what a control may do and how it reads. */
export const ServiceControls = {
  /** Whether a service is stopped: the whole environment, or the service itself. */
  isStopped: (stop: TargetStop | null | undefined, service: string): boolean =>
    Boolean(stop && (stop.environment || stop.services.includes(service))),

  /** Whether anything is stopped. */
  anyStopped: (stop: TargetStop | null | undefined): stop is TargetStop =>
    Boolean(stop && (stop.environment || stop.services.length > 0)),

  /** The controls a service offers: a stopped one starts (unless its whole environment is stopped, which starts whole);
   * a running one stops or restarts. */
  forService: (stop: TargetStop | null | undefined, service: string): ControlOperation[] =>
    stop?.environment
      ? []
      : ServiceControls.isStopped(stop, service)
        ? [ControlOperation.Start]
        : [ControlOperation.Restart, ControlOperation.Stop],

  /** The control an environment offers: start while anything is stopped, else stop. */
  forEnvironment: (stop: TargetStop | null | undefined): ControlOperation =>
    ServiceControls.anyStopped(stop) ? ControlOperation.Start : ControlOperation.Stop,

  /** Whether the console offers controls on a product: never on Wheelhouse itself, which would stop its own console. */
  offers: (product: string | null | undefined, selfProduct: string): boolean => Boolean(product) && product !== selfProduct,

  /** Whether a control needs the target ID typed and a reason: prod does. */
  isGuarded: (target: ControlTarget): boolean => target.environment === 'prod',

  /** A control's name: `Stop management`, `Start the environment`, `Reset the database`, `Reset the identity
   * migrations`, `Run admin create on identity`; a run without its label reads `Run a command on identity`. */
  label: (
    operation: ControlOperation,
    service: string | null,
    set?: string | null,
    command?: string | null,
    step?: string | null,
  ): string =>
    operation === ControlOperation.FilesMigration
      ? step
        ? `Files migration: ${step}`
        : 'Files migration'
      : operation === ControlOperation.Reset
      ? set
        ? `Reset the ${set} migrations`
        : 'Reset the database'
      : operation === ControlOperation.Run
        ? `Run ${ServiceControls.commandWords(command)} on ${service ?? 'a service'}`
        : `${ServiceControls.verb(operation)} ${service ?? 'the environment'}`,

  /** A recorded control's name, as lists show it. */
  labelOf: (operation: DeploymentOperation): string =>
    ServiceControls.label(
      operation.kind,
      operation.services.length ? operation.services.join(', ') : null,
      operation.set,
      operation.command,
      operation.step,
    ),

  /** A request's name; a run's arguments never name it, only its recorded label does. */
  labelOfRequest: (request: ControlRequest): string =>
    ServiceControls.label(request.operation, request.service, request.set?.name),

  /** A request's confirmation question: `Stop management on foreverpin-dev?`, `Run a command in identity on
   * ocharo-prod?`. */
  titleOf: (request: ControlRequest): string =>
    request.operation === ControlOperation.Run
      ? `Run a command in ${request.service ?? 'a service'} on ${request.target.id}?`
      : `${ServiceControls.labelOfRequest(request)} on ${request.target.id}?`,

  /** The words a masked command label names its run by: what it runs before its first option, without the `dotnet
   * X.dll` launcher — `dotnet Identity.Api.dll admin create --slug …` reads `admin create`; none reads `a command`. */
  commandWords: (command: string | null | undefined): string => {
    const words = (command ?? '').split(/\s+/).filter(Boolean);
    const option = words.findIndex((word) => word.startsWith('-'));
    const lead = option < 0 ? words : words.slice(0, option);
    const named = lead[0] === 'dotnet' && lead[1]?.toLowerCase().endsWith('.dll') ? lead.slice(2) : lead;
    return named.join(' ') || 'a command';
  },

  /** How a run ended, in one line: `Exited with code 0 in 3s`, `Timed out after 2m 0s`. */
  runResult: (run: ControlRun): string =>
    run.status === 'timed out'
      ? `Timed out after ${Measures.duration(run.seconds)}`
      : run.exitCode === null
        ? `Ended without an exit code in ${Measures.duration(run.seconds)}`
        : `Exited with code ${run.exitCode} in ${Measures.duration(run.seconds)}`,

  /** An operation's verb, capitalised. */
  verb: (operation: ControlOperation): string =>
    operation === ControlOperation.Stop
      ? 'Stop'
      : operation === ControlOperation.Start
        ? 'Start'
        : operation === ControlOperation.Reset
          ? 'Reset'
          : operation === ControlOperation.Run
            ? 'Run'
            : operation === ControlOperation.FilesMigration
              ? 'Move files'
              : 'Restart',

  /** What a stopped environment's state reads: `Stopped`, or `management stopped`. */
  stateLabel: (stop: TargetStop): string =>
    stop.environment ? 'Stopped' : `${stop.services.join(', ')} stopped`,

  /** What a control does, for its confirmation. */
  consequence: (request: ControlRequest): string => {
    const subject = request.service ?? request.target.id;
    if (request.operation === ControlOperation.Reset && request.set)
      return `The ${request.set.name} migration set on ${request.target.id} is rolled back to level 0 by the release it `
        + `runs, after a snapshot of the database; ${request.set.service} stops until the next deploy's migrate step `
        + 'builds the set again and starts it. The other services keep running, and the rest of the database keeps its '
        + 'data.';
    if (request.operation === ControlOperation.Reset)
      return `${subject}'s database is snapshotted, then dropped and created again, empty. Every service stops until the `
        + "next deploy's migrate step builds the schema and starts them; the snapshot stays on the target.";
    if (request.operation === ControlOperation.Run)
      return `The command runs once in a one-off container of ${subject}'s running release, with its settings and `
        + `database network on ${request.target.id}. The services keep running and nothing stopped changes. The exit `
        + 'code and the last output line stay on the job; the arguments do not.';
    if (request.operation === ControlOperation.FilesMigration)
      return `${subject}'s legacy files move into Files; the services keep running and nothing stopped changes.`;
    if (request.operation === ControlOperation.Restart)
      return `${subject} stops and starts again on the release it runs.`;
    if (request.operation === ControlOperation.Start)
      return `${subject} starts again on the release it runs; nothing is pulled.`;
    return `${subject} stops; containers, volumes and routes stay. Deploys wait until it starts again.`;
  },
} as const;
