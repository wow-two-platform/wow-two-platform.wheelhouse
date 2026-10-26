/**
 * Service controls (decision 45): the owner stops, starts or restarts the release an environment runs — one service, or
 * the whole environment for a stop or a start. A stop keeps containers, volumes and routes; every deploy to an
 * environment with something stopped is refused until a start, so an automatic deploy never starts it silently.
 */

/** What a control does. */
export const ControlOperation = {
  Stop: 'stop',
  Start: 'start',
  Restart: 'restart',
} as const;
export type ControlOperation = (typeof ControlOperation)[keyof typeof ControlOperation];

/** A control as the history records it: its kind and the service it named; none for the whole environment. */
export interface DeploymentOperation {
  kind: ControlOperation;
  services: string[];
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

/** One control the owner asks for: what, on which environment, and which service (none for the environment). */
export interface ControlRequest {
  readonly target: ControlTarget;
  readonly operation: ControlOperation;
  readonly service: string | null;
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

  /** A control's name: `Stop management`, `Start the environment`. */
  label: (operation: ControlOperation, service: string | null): string =>
    `${ServiceControls.verb(operation)} ${service ?? 'the environment'}`,

  /** A recorded control's name, as lists show it. */
  labelOf: (operation: DeploymentOperation): string =>
    ServiceControls.label(operation.kind, operation.services.length ? operation.services.join(', ') : null),

  /** An operation's verb, capitalised. */
  verb: (operation: ControlOperation): string =>
    operation === ControlOperation.Stop ? 'Stop' : operation === ControlOperation.Start ? 'Start' : 'Restart',

  /** What a stopped environment's state reads: `Stopped`, or `management stopped`. */
  stateLabel: (stop: TargetStop): string =>
    stop.environment ? 'Stopped' : `${stop.services.join(', ')} stopped`,

  /** What a control does, for its confirmation. */
  consequence: (request: ControlRequest): string => {
    const subject = request.service ?? request.target.id;
    if (request.operation === ControlOperation.Restart)
      return `${subject} stops and starts again on the release it runs.`;
    if (request.operation === ControlOperation.Start)
      return `${subject} starts again on the release it runs; nothing is pulled.`;
    return `${subject} stops; containers, volumes and routes stay. Deploys wait until it starts again.`;
  },
} as const;
