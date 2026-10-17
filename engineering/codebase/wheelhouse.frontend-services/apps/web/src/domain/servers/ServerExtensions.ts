import { Measures } from '@/domain/common';
import type {
  ContainerVitals,
  DiskUsage,
  HostVitals,
  ServerHostVitals,
  ServerVitals,
  TargetVitals,
} from './models/ServerVitals';

/** Meter zones `[good, warn]` in percent: green up to 75, amber up to 90, red above. */
export const USAGE_THRESHOLDS: [number, number] = [75, 90];

/** Restarts at or above this count mark a container as unstable. */
export const RESTART_WARNING = 3;

/** The indicator tone for a container. */
export type ContainerTone = 'success' | 'info' | 'warning' | 'destructive' | 'neutral';

/** Pure rules over host and container vitals. */
export const ServerExtensions = {
  /** Used memory in percent, or null when the host hides it. */
  memoryPercent: (host: HostVitals) =>
    host.memoryTotalBytes && host.memoryAvailableBytes != null
      ? Measures.usedPercent(host.memoryTotalBytes, host.memoryAvailableBytes)
      : null,

  /** The 1-minute load against the CPU count, in percent. */
  loadPercent: (host: HostVitals) => (host.load && host.cpus ? (host.load[0] / host.cpus) * 100 : null),

  /** Used disk in percent. */
  diskPercent: (disk: DiskUsage) => Measures.usedPercent(disk.totalBytes, disk.freeBytes),

  /** Whether a container is running and not failing its health check. */
  isHealthy: (container: ContainerVitals) =>
    container.state === 'running' && (container.health == null || container.health === 'healthy'),

  /** The indicator tone for a container's state and health. */
  containerTone: (container: ContainerVitals): ContainerTone =>
    container.state !== 'running'
      ? 'destructive'
      : container.health === 'unhealthy'
        ? 'destructive'
        : container.health === 'starting'
          ? 'info'
          : container.restarts >= RESTART_WARNING
            ? 'warning'
            : 'success',

  /** Human text for a container's state: `healthy`, `starting`, `exited (137)`. */
  containerLabel: (container: ContainerVitals) =>
    container.state === 'running'
      ? (container.health ?? 'running')
      : `${container.state ?? 'unknown'}${container.exitCode != null ? ` (${container.exitCode})` : ''}`,

  /** Targets grouped by the host they run on, in first-seen order. */
  byServer: (targets: TargetVitals[]) => {
    const groups = new Map<string, TargetVitals[]>();
    for (const target of targets) groups.set(target.serverId, [...(groups.get(target.serverId) ?? []), target]);
    return groups;
  },

  /** One server's own host reading from a pass, or null when the pass did not include the server. */
  readingOf: (vitals: ServerVitals | undefined, serverId: string): ServerHostVitals | null =>
    vitals?.servers.find((server) => server.serverId === serverId) ?? null,

  /** One server's host vitals, read from the server itself, so a server that runs no target has them too. */
  hostOf: (vitals: ServerVitals | undefined, serverId: string): HostVitals | null => {
    const reading = ServerExtensions.readingOf(vitals, serverId);
    return reading?.ok ? (reading.host ?? null) : null;
  },
} as const;
