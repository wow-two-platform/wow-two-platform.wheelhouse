import type { DeploymentStep } from '../../models/DeploymentStep';
import type { SiteProbe } from '../../models/PublishedSite';
import type { DeploymentItem } from './DeploymentItem';

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
}
