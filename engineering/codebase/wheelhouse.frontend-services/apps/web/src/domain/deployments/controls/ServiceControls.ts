/**
 * Service controls (decision 45): the owner stops, starts or restarts the release an environment runs — one service, or
 * the whole environment for a stop or a start. A stop keeps containers, volumes and routes; every deploy to an
 * environment with something stopped is refused until a start, so an automatic deploy never starts it silently. A reset
 * (v0.9) snapshots the environment's database, drops and creates it again empty, and leaves every service stopped for
 * the next deploy, whose migrate step builds the schema and starts them. A set reset rolls one migration set back to 0
 * with the running release's own scripts instead, and stops only the service hosting it; the next deploy builds the
 * set again and starts it.
 */

/** What a control does. */
export const ControlOperation = {
  Stop: 'stop',
  Start: 'start',
  Restart: 'restart',
  Reset: 'reset',
} as const;
export type ControlOperation = (typeof ControlOperation)[keyof typeof ControlOperation];

/** A control as the history records it: its kind and the service it named, none for the whole environment; a set
 * reset also names its migration set. */
export interface DeploymentOperation {
  kind: ControlOperation;
  services: string[];
  set?: string | null | undefined;
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
 * of one migration set names it. */
export interface ControlRequest {
  readonly target: ControlTarget;
  readonly operation: ControlOperation;
  readonly service: string | null;
  readonly set?: ControlSet | null;
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
   * migrations`. */
  label: (operation: ControlOperation, service: string | null, set?: string | null): string =>
    operation === ControlOperation.Reset
      ? set
        ? `Reset the ${set} migrations`
        : 'Reset the database'
      : `${ServiceControls.verb(operation)} ${service ?? 'the environment'}`,

  /** A recorded control's name, as lists show it. */
  labelOf: (operation: DeploymentOperation): string =>
    ServiceControls.label(operation.kind, operation.services.length ? operation.services.join(', ') : null, operation.set),

  /** A request's name, as its confirmation titles it. */
  labelOfRequest: (request: ControlRequest): string =>
    ServiceControls.label(request.operation, request.service, request.set?.name),

  /** An operation's verb, capitalised. */
  verb: (operation: ControlOperation): string =>
    operation === ControlOperation.Stop
      ? 'Stop'
      : operation === ControlOperation.Start
        ? 'Start'
        : operation === ControlOperation.Reset
          ? 'Reset'
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
    if (request.operation === ControlOperation.Restart)
      return `${subject} stops and starts again on the release it runs.`;
    if (request.operation === ControlOperation.Start)
      return `${subject} starts again on the release it runs; nothing is pulled.`;
    return `${subject} stops; containers, volumes and routes stay. Deploys wait until it starts again.`;
  },
} as const;
