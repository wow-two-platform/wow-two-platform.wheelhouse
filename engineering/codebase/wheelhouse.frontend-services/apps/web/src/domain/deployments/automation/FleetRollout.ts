import type { DeploymentPromotion } from '../history/models/DeploymentPromotion';
import { SelfDeployRules } from '../SelfDeployRules';

/** Where a fleet rollout stands. */
export const FleetStatus = { Running: 'running', Succeeded: 'succeeded', Failed: 'failed', Cancelled: 'cancelled' } as const;
export type FleetStatus = (typeof FleetStatus)[keyof typeof FleetStatus];

/** Where one product of a fleet stands. */
export const FleetItemStatus = {
  Waiting: 'waiting',
  Running: 'running',
  Succeeded: 'succeeded',
  Failed: 'failed',
  Refused: 'refused',
  NotStarted: 'not_started',
} as const;
export type FleetItemStatus = (typeof FleetItemStatus)[keyof typeof FleetItemStatus];

/** One product of a fleet: the release it promotes to one prod target, and its outcome. */
export interface FleetRolloutItem {
  readonly position: number;
  readonly product: string;
  readonly target: string;
  readonly bundleId: string;
  readonly release: string | null;
  /** What prod ran when the fleet started; null when it ran nothing. */
  readonly fromRelease: string | null;
  readonly status: FleetItemStatus;
  readonly deploymentId: string | null;
  /** Why it failed or was refused, or a note on its success. */
  readonly detail: string | null;
  readonly startedAt: string | null;
  readonly completedAt: string | null;
}

/** Several products promoted to prod in one run, in order, stopping at the first failure. */
export interface FleetRollout {
  readonly id: string;
  readonly actor: string;
  readonly reason: string | null;
  readonly overrideFreeze: boolean;
  readonly status: FleetStatus;
  readonly createdAt: string;
  readonly completedAt: string | null;
  readonly cancelRequestedBy: string | null;
  readonly cancelRequestedAt: string | null;
  readonly items: readonly FleetRolloutItem[];
}

/** One release a fleet promotes, as the owner confirmed it. */
export interface FleetSelection {
  readonly target: string;
  readonly release: string;
  /** The target ID, typed; prod on the local server needs it. */
  readonly confirm?: string;
}

/** How a fleet's products ended: the counts the progress line names. */
export interface FleetProgress {
  /** Products whose rollout ended, however it went. */
  readonly done: number;
  readonly total: number;
  readonly succeeded: number;
  /** The product the fleet is on, while it runs one. */
  readonly current: FleetRolloutItem | null;
}

/** The most products one fleet promotes, as the host enforces. */
export const FleetMaxItems = 20;

/** Pure rules of fleet rollouts. */
export const FleetRollouts = {
  /** The fleet that runs, if any; one runs at a time. */
  active(fleets: readonly FleetRollout[] | undefined): FleetRollout | null {
    return fleets?.find((fleet) => fleet.status === FleetStatus.Running) ?? null;
  },

  /** How far a fleet got. */
  progress(fleet: FleetRollout): FleetProgress {
    const ended = fleet.items.filter((item) => item.status !== FleetItemStatus.Waiting && item.status !== FleetItemStatus.Running);
    return {
      done: ended.filter((item) => item.status !== FleetItemStatus.NotStarted).length,
      total: fleet.items.length,
      succeeded: fleet.items.filter((item) => item.status === FleetItemStatus.Succeeded).length,
      current: fleet.items.find((item) => item.status === FleetItemStatus.Running) ?? null,
    };
  },

  /** The prod targets a running fleet still holds: the one it is on and those waiting. */
  heldTargets(fleet: FleetRollout | null): ReadonlySet<string> {
    return new Set(
      (fleet?.status === FleetStatus.Running ? fleet.items : [])
        .filter((item) => item.status === FleetItemStatus.Waiting || item.status === FleetItemStatus.Running)
        .map((item) => item.target),
    );
  },

  /** Whether the owner may still cancel it: it runs and nobody asked yet. */
  canCancel(fleet: FleetRollout): boolean {
    return fleet.status === FleetStatus.Running && fleet.cancelRequestedAt === null;
  },

  /** One line on how a fleet stands: `2 of 3 promoted · on ocharo`, `Stopped at ocharo`. */
  summary(fleet: FleetRollout): string {
    const progress = FleetRollouts.progress(fleet);
    const stopped = fleet.items.find((item) => item.status === FleetItemStatus.Failed || item.status === FleetItemStatus.Refused);
    switch (fleet.status) {
      case FleetStatus.Running:
        return (
          `${progress.succeeded} of ${progress.total} promoted` +
          (progress.current ? ` · on ${progress.current.product}` : '') +
          (fleet.cancelRequestedAt ? ' · stopping after this product' : '')
        );
      case FleetStatus.Succeeded:
        return `All ${progress.total} promoted`;
      case FleetStatus.Cancelled:
        return `Cancelled after ${progress.succeeded} of ${progress.total}`;
      default:
        return stopped ? `Stopped at ${stopped.product}: ${progress.succeeded} of ${progress.total} promoted` : 'Stopped';
    }
  },

  /** The releases Ready to promote a fleet takes, in the order the panel lists them; held or unknown ones are left
   *  out, and so is Wheelhouse itself, which is promoted on its own since its console restarts. */
  selection(
    offers: readonly DeploymentPromotion[] | undefined,
    chosen: ReadonlySet<string>,
    held: ReadonlySet<string> = new Set(),
  ): DeploymentPromotion[] {
    return (offers ?? [])
      .filter((offer) => chosen.has(FleetRollouts.key(offer)) && !held.has(offer.prodTargetId)
        && !SelfDeployRules.isSelfProduct(offer.product))
      .slice(0, FleetMaxItems);
  },

  /** The key a Ready to promote row is selected by. */
  key(offer: Pick<DeploymentPromotion, 'prodTargetId' | 'bundleId'>): string {
    return `${offer.prodTargetId}|${offer.bundleId}`;
  },

  /** The words for a product's status in a fleet. */
  itemLabel(status: FleetItemStatus): string {
    return {
      waiting: 'Waiting',
      running: 'Running',
      succeeded: 'Promoted',
      failed: 'Failed',
      refused: 'Refused',
      not_started: 'Not started',
    }[status];
  },
} as const;
