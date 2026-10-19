/** How a rollout treated one service. */
export interface ServiceRolloutService {
  readonly name: string;
  /** `added`, `updated` (its definition or image differs) or `unchanged`. */
  readonly change: string;
  /** `unchanged`, `pending`, `updated`, `failed`, `not_started` or `rolled_back`. */
  readonly outcome: string;
  readonly dependsOn: readonly string[];
}

/** How a rollout treated each service: what changed, the dependency waves it started them in and how each ended. */
export interface ServiceRollout {
  /** Whether the changed services started wave by wave rather than in one Compose `up`. */
  readonly staged: boolean;
  readonly waves: readonly (readonly string[])[];
  readonly removed: readonly string[];
  readonly services: readonly ServiceRolloutService[];
}

/** What a service's outcome reads as, beside the services list. */
export interface ServiceOutcomeReading {
  readonly label: string;
  readonly tone: 'neutral' | 'success' | 'danger' | 'warning' | 'info';
  /** The 1-based wave it started in, when the rollout started its services wave by wave. */
  readonly wave: number | null;
}

/** Pure rules of service-aware rollouts. */
export const ServiceRollouts = {
  /** How one service of a rollout ended; null when the rollout reported no service outcomes or not this one. */
  outcome(rollout: ServiceRollout | null | undefined, service: string): ServiceOutcomeReading | null {
    const entry = rollout?.services.find((item) => item.name === service);
    if (!rollout || !entry) return null;
    const index = rollout.staged ? rollout.waves.findIndex((wave) => wave.includes(service)) : -1;
    const wave = index >= 0 ? index + 1 : null;
    switch (entry.outcome) {
      case 'unchanged':
        return { label: 'kept running', tone: 'neutral', wave: null };
      case 'updated':
        return { label: entry.change === 'added' ? 'started' : 'replaced', tone: 'success', wave };
      case 'failed':
        return { label: 'failed', tone: 'danger', wave };
      case 'not_started':
        return { label: 'not started', tone: 'warning', wave };
      case 'rolled_back':
        return { label: 'rolled back', tone: 'warning', wave };
      default:
        return { label: 'pending', tone: 'info', wave };
    }
  },
} as const;
