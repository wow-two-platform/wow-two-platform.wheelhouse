import type { DeploymentTarget } from './models/DeploymentTarget';
import type { ReleaseArtifact } from './models/ReleaseArtifact';
import type { ServiceVersion } from './models/ServiceVersion';
import type { TargetState } from './models/TargetState';
import { environmentRank } from '@/domain/targets';

/** Taking one environment's release to the next environment along the path. */
export interface Promotion {
  targetId: string;
  environment: string;
  /** The catalog bundle carrying the release. */
  bundleId: string;
  release: string;
}

/** One environment of a product, as the comparison shows it. */
export interface EnvironmentColumn {
  targetId: string;
  environment: string;
  /** False until the target's state was read. */
  read: boolean;
  release: string | null;
  /** A commit's build, which only dev takes, cannot be promoted. */
  candidate: boolean;
  promotion: Promotion | null;
}

/** One service across a product's environments; `pending[i]` marks a version the next environment lacks. */
export interface ServiceComparison {
  service: string;
  versions: (ServiceVersion | null)[];
  pending: boolean[];
}

/** A product's environments side by side, services as rows. */
export interface EnvironmentComparison {
  columns: EnvironmentColumn[];
  rows: ServiceComparison[];
}

/** Lines up a product's environments along the promotion path and finds what each could hand to the next.
 * `states` aligns with `targets`; an entry is undefined while it is unread. */
export function compareEnvironments(
  targets: readonly DeploymentTarget[],
  states: readonly (TargetState | null | undefined)[],
  releases: readonly ReleaseArtifact[],
): EnvironmentComparison {
  const ordered = targets
    .map((target, index) => ({ target, state: states[index] }))
    .sort((left, right) => environmentRank(left.target.environment) - environmentRank(right.target.environment)
      || left.target.id.localeCompare(right.target.id));
  const columns: EnvironmentColumn[] = ordered.map(({ target, state }) => ({
    targetId: target.id,
    environment: target.environment,
    read: state !== undefined,
    release: state?.current?.release ?? null,
    candidate: state?.current?.kind === 'candidate',
    promotion: null,
  }));
  columns.forEach((column, index) => {
    const next = columns[index + 1];
    if (!next || !column.release || column.candidate || !next.read || column.release === next.release) return;
    const bundle = releases.find((artifact) => artifact.product === ordered[index]!.target.product
      && artifact.kind === 'release' && artifact.release === column.release);
    if (bundle)
      column.promotion = { targetId: next.targetId, environment: next.environment, bundleId: bundle.id,
        release: column.release };
  });
  const services = [...new Set(ordered.flatMap(({ state }) => Object.keys(state?.current?.versions ?? {})))].sort();
  const rows = services.map((service) => {
    const versions = ordered.map(({ state }) => state?.current?.versions?.[service] ?? null);
    return {
      service,
      versions,
      pending: versions.map((version, index) => index + 1 < versions.length && columns[index + 1]!.read
        && version !== null && version.version !== versions[index + 1]?.version),
    };
  });
  return { columns, rows };
}
