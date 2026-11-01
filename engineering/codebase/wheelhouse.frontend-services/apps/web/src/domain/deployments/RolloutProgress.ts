import { DeploymentExtensions } from './DeploymentExtensions';
import { JobStatus } from './enums/JobStatus';
import { StepStatus } from './enums/StepStatus';
import type { ServiceRollout } from './history/ServiceRollout';
import type { DeploymentStep } from './models/DeploymentStep';

/** What one version of a service is doing in a rollout, as the mark beside it says. */
export const ServiceMark = {
  /** The version serves: untouched by the release, or still up while its replacement waits. */
  Running: 'running',
  /** The old version, stopped for its replacement. */
  Stopped: 'stopped',
  /** The new version started and turned healthy. */
  Healthy: 'healthy',
  /** The new version started, then did not stay healthy. */
  Unhealthy: 'unhealthy',
  Starting: 'starting',
  /** The new version waits for its wave. */
  Waiting: 'waiting',
  Failed: 'failed',
  /** The new version never started: a wave before it failed. */
  NotStarted: 'not_started',
  /** The new version started, then the roll back replaced it. */
  RolledBack: 'rolled_back',
  /** The old version serves again after a roll back. */
  Restored: 'restored',
  /** The release dropped the service. */
  Removed: 'removed',
  /** The rollout ended without saying how this version fared. */
  Unknown: 'unknown',
} as const;
export type ServiceMark = (typeof ServiceMark)[keyof typeof ServiceMark];

/** Each mark's words, for its legend and for assistive technology. */
export const ServiceMarkWords: Readonly<Record<ServiceMark, string>> = {
  running: 'running',
  stopped: 'stopped',
  healthy: 'started, healthy',
  unhealthy: 'started, not healthy',
  starting: 'starting',
  waiting: 'waiting',
  failed: 'failed',
  not_started: 'not started',
  rolled_back: 'rolled back',
  restored: 'running again',
  removed: 'removed',
  unknown: 'unknown',
};

/** One version of a service in a rollout: the version, or null where there is none, and how it fared. */
export interface ServiceVersionCell {
  readonly version: string | null;
  readonly mark: ServiceMark | null;
}

/** One service of a rollout: what ran before and what the release brings. */
export interface RolloutServiceRow {
  readonly service: string;
  /** Whether the release changes the service: a new image, definition or settings, or a new or dropped service. */
  readonly changed: boolean;
  readonly current: ServiceVersionCell;
  /** What the release brings; null when the service keeps running unchanged. */
  readonly next: ServiceVersionCell | null;
}

/** The step's name when it starts named services (`Start desk, jobs`), or null; `Start containers` starts the rest. */
function startingNow(steps: readonly DeploymentStep[] | null | undefined): ReadonlySet<string> | 'all' {
  const running = [...(steps ?? [])].reverse().find((step) => step.status === StepStatus.Running);
  if (!running) return new Set();
  if (running.name === 'Start containers') return 'all';
  return running.name.startsWith('Start ') ? new Set(running.name.slice('Start '.length).split(', ')) : new Set();
}

function plural(count: number, noun: string): string {
  return `${count} ${noun}${count === 1 ? '' : 's'}`;
}

/** Pure rules of a rollout followed live: its title, its services table and its one-line summary. */
export const RolloutProgress = {
  /**
   * The follow view's title, by where the rollout stands; the release itself stays out of it, since a set's label runs
   * long and the services table names each version.
   */
  title(status: JobStatus | null | undefined, rollout?: ServiceRollout | null): string {
    if (!status || DeploymentExtensions.isPending(status)) return 'Deploying updated services';
    switch (status) {
      case JobStatus.Succeeded:
        return rollout?.services.length && !rollout.services.some((item) => item.change !== 'unchanged')
          ? 'No service needed an update'
          : 'Deployed updated services';
      case JobStatus.Failed:
        return 'Deploy failed';
      case JobStatus.RolledBack:
        return 'Deploy rolled back';
      case JobStatus.RollbackFailed:
        return 'Deploy and its roll back failed';
      case JobStatus.Interrupted:
        return 'Deploy interrupted';
      case JobStatus.Rejected:
        return 'Deploy refused';
      default:
        return 'Deploy outcome unknown';
    }
  },

  /**
   * Each service as a row: what changed first, in the order the rollout starts it, then what kept running, then what
   * the release dropped. `steps` tells which services start right now; `status` tells whether the rollout still runs.
   */
  rows(
    rollout: ServiceRollout | null | undefined,
    status: JobStatus | null | undefined,
    steps?: readonly DeploymentStep[] | null,
  ): RolloutServiceRow[] {
    if (!rollout) return [];
    const live = !status || DeploymentExtensions.isPending(status);
    const starting = live ? startingNow(steps) : new Set<string>();
    const order = rollout.waves.flat();
    const rank = (name: string, changed: boolean) => {
      const at = order.indexOf(name);
      return changed ? (at >= 0 ? at : order.length) : order.length + 1;
    };
    const rows = rollout.services.map((item): RolloutServiceRow => {
      const from = item.from ?? null;
      const to = item.to ?? null;
      const ran = from ? ServiceMark.Running : null;
      const replaced = from ? ServiceMark.Stopped : null;
      switch (item.outcome) {
        case 'unchanged':
          return { service: item.name, changed: false, current: { version: from ?? to, mark: ServiceMark.Running }, next: null };
        case 'updated':
          return {
            service: item.name,
            changed: true,
            current: { version: from, mark: replaced },
            next: { version: to, mark: item.health === 'unhealthy' ? ServiceMark.Unhealthy : ServiceMark.Healthy },
          };
        case 'failed':
          return { service: item.name, changed: true, current: { version: from, mark: replaced }, next: { version: to, mark: ServiceMark.Failed } };
        case 'not_started':
          return { service: item.name, changed: true, current: { version: from, mark: ran }, next: { version: to, mark: ServiceMark.NotStarted } };
        case 'rolled_back':
          return {
            service: item.name,
            changed: true,
            current: { version: from, mark: from ? ServiceMark.Restored : null },
            next: { version: to, mark: ServiceMark.RolledBack },
          };
        default: {
          const mark = !live
            ? ServiceMark.Unknown
            : starting === 'all' || starting.has(item.name)
              ? ServiceMark.Starting
              : ServiceMark.Waiting;
          return { service: item.name, changed: true, current: { version: from, mark: ran }, next: { version: to, mark } };
        }
      }
    });
    const removed = rollout.removed.map(
      (name): RolloutServiceRow => ({
        service: name,
        changed: true,
        current: { version: null, mark: null },
        next: { version: null, mark: ServiceMark.Removed },
      }),
    );
    return [
      ...rows
        .map((row, index) => ({ row, index }))
        .sort((left, right) => {
          const byRank = rank(left.row.service, left.row.changed) - rank(right.row.service, right.row.changed);
          return byRank || (left.row.changed ? left.index - right.index : left.row.service.localeCompare(right.row.service));
        })
        .map(({ row }) => row),
      ...removed,
    ];
  },

  /** One line on what the rollout changes: `1 of 10 services changed; 9 kept running`, as the runner words it. */
  summary(rollout: ServiceRollout | null | undefined): string {
    const services = rollout?.services ?? [];
    if (!services.length) return '';
    const changed = services.filter((item) => item.change !== 'unchanged').length;
    const kept = services.length - changed;
    const line =
      changed === 0
        ? `No service changed; ${kept} kept running`
        : kept === 0
          ? `${plural(changed, 'service')} changed`
          : `${changed} of ${plural(services.length, 'service')} changed; ${kept} kept running`;
    return rollout?.removed.length ? `${line}; removed ${rollout.removed.join(', ')}` : line;
  },

  /** The marks a table shows, in the legend's order, so the legend names only what is on screen. */
  marks(rows: readonly RolloutServiceRow[]): ServiceMark[] {
    const shown = new Set(rows.flatMap((row) => [row.current.mark, row.next?.mark ?? null]).filter((mark) => mark !== null));
    return Object.values(ServiceMark).filter((mark) => shown.has(mark));
  },
} as const;
