import type { TargetCondition } from '@/domain/deployments';
import type { TargetStop } from '@/domain/deployments/controls';

/** One filesystem's capacity on a host. */
export interface DiskUsage {
  path: string;
  totalBytes: number;
  freeBytes: number;
}

/** A host's load, memory, disks and uptime; a field is null where the host hides it. */
export interface HostVitals {
  cpus: number | null;
  load: [number, number, number] | null;
  memoryTotalBytes: number | null;
  memoryAvailableBytes: number | null;
  uptimeSeconds: number | null;
  disks: DiskUsage[];
}

/** One container's state and resource use; never its environment or logs. */
export interface ContainerVitals {
  service: string;
  state: string | null;
  health: string | null;
  restarts: number;
  startedAt: string | null;
  exitCode: number | null;
  cpuPercent: number | null;
  memoryBytes: number | null;
  memoryLimitBytes: number | null;
}

/** One server's own host, read whether or not it runs a target; `ok` is false when the server could not be read. */
export interface ServerHostVitals {
  serverId: string;
  ok: boolean;
  reason?: string | null;
  host?: HostVitals | null;
  problems?: string[];
}

/** What one target's containers looked like; `ok` is false when the target could not be read. Its host is its
 * server's, in `ServerVitals.servers`. */
export interface TargetVitals {
  targetId: string;
  serverId: string;
  ok: boolean;
  reason?: string | null;
  project?: string;
  containers?: ContainerVitals[] | null;
  problems?: string[];
  condition?: TargetCondition;
  release?: string | null;
  /** What the owner keeps stopped there; its containers read stopped, never failing. */
  stopped?: TargetStop | null;
}

/** Every server's host and every target's containers from one collection pass. */
export interface ServerVitals {
  collectedAt: string;
  servers: ServerHostVitals[];
  targets: TargetVitals[];
}
