/** One stored reading: a server's own host figures (`targetId` null) or one target's container health. A figure is
 * null where the host hid it, the reading does not carry it, or the server or target could not be read. */
export interface VitalsSample {
  targetId: string | null;
  serverId: string;
  sampledAt: string;
  readable: boolean;
  loadPercent: number | null;
  memoryPercent: number | null;
  diskPercent: number | null;
  containers: number;
  healthyContainers: number;
  restarts: number;
}

/** The trend ranges the servers page offers, in hours. */
export const TrendRanges = { day: 24, week: 168, month: 720 } as const;
export type TrendRange = keyof typeof TrendRanges;

/** One series point for a trend line. */
export interface TrendPoint {
  at: number;
  value: number;
}

/** A server's host figures over time, from its own host readings. */
export function hostTrend(
  samples: readonly VitalsSample[],
  serverId: string,
  figure: 'loadPercent' | 'memoryPercent' | 'diskPercent',
): TrendPoint[] {
  return samples
    .filter((sample) => sample.serverId === serverId && sample.targetId === null && sample[figure] !== null)
    .map((sample) => ({ at: Date.parse(sample.sampledAt), value: sample[figure] as number }))
    .filter((point) => Number.isFinite(point.at));
}
