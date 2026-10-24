import { ReleaseSets } from "@/domain/deployments/ReleaseSets";
import type { TopologyService } from "@/domain/topology";

/** A service's running version as the map shows it, and where that version comes from. */
export interface ServiceVersionLabel {
  readonly label: string;
  readonly detail: string;
  /** False when the release records no version for this service and the release name stands in. */
  readonly isServiceVersion: boolean;
}

/**
 * Every service shows a version: its own when the release records one, otherwise the release it runs.
 * A release built before per-service versions still names what is deployed.
 */
export function describeServiceVersion(
  service: Pick<TopologyService, "version">,
  release: string | null,
): ServiceVersionLabel {
  if (service.version)
    return {
      label: service.version.version,
      detail: `Last changed in ${ReleaseSets.text(service.version.changedIn)}`,
      isServiceVersion: true,
    };
  if (release)
    return {
      // A set's label stands in as a pill does (`platform 0.1.8 +2`); its detail names every repository's release.
      label: ReleaseSets.short(release),
      detail: `Release ${ReleaseSets.text(release)}; it records no per-service versions`,
      isServiceVersion: false,
    };
  return {
    label: "unversioned",
    detail: "The deployed release is not recorded",
    isServiceVersion: false,
  };
}
