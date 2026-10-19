import type { DeploymentStep } from '../../models/DeploymentStep';
import type { SiteProbe } from '../../models/PublishedSite';
import type { DeploymentItem } from './DeploymentItem';
import type { ServiceRollout } from '../ServiceRollout';

/** One service of a rollout against what its target ran before. */
export interface ServiceChange {
  service: string;
  /** The version (or short image digest) before; null when the target ran none. */
  from: string | null;
  /** The version deployed; null when the release dropped the service. */
  to: string | null;
  changed: boolean;
}

/** Another deployment a detail points at. */
export interface DeploymentReference {
  id: string;
  release: string | null;
  bundleId: string | null;
}

/** A site a rollout published, with whether it answered right after. */
export interface DeploymentSite {
  name: string;
  service: string | null;
  exposure: string | null;
  url: string;
  probe: SiteProbe | null;
}

/** The restore point a prod rollout took before it changed anything, and the commands that restore it. */
export interface DeploymentSnapshot {
  /** The snapshot's folder on the target. */
  path: string;
  createdAt: string | null;
  databases: Array<{ service: string | null; host: string; port: number; database: string; file: string; bytes: number | null }>;
  volumes: Array<{ volume: string; file: string; bytes: number | null }>;
  /** Run on the target, in order; a database restore reads its password from PGPASSWORD. */
  restore: string[];
}

/** One deployment in full. */
export interface DeploymentDetail {
  deployment: DeploymentItem;
  /** Null when the runner no longer holds the bundle. */
  services: ServiceChange[] | null;
  /** The target's previous success. */
  previous: DeploymentReference | null;
  /** What a roll back restores; null unless this is what the target runs and its release permits it. */
  rollback: DeploymentReference | null;
  steps: DeploymentStep[];
  sites: DeploymentSite[];
  reconciledBy: string | null;
  /** Whether the target's report could be read; false leaves steps and sites empty. */
  observed: boolean;
  /** The restore point a prod rollout took; null when it took none. */
  snapshot: DeploymentSnapshot | null;
  /** How the rollout treated each service; null when the target did not report it. */
  serviceRollout: ServiceRollout | null;
}
