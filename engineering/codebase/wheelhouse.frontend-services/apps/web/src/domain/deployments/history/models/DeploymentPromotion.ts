import type { DeploymentVerification } from '../../automation/DevCheck';

/** The newest release that succeeded on a product's dev and is not what its prod runs. */
export interface DeploymentPromotion {
  product: string;
  workspace: string | null;
  release: string;
  /** The bundle a promotion submits. */
  bundleId: string;
  publishedAt: string | null;
  devTargetId: string;
  devSucceededAt: string;
  prodTargetId: string;
  /** What prod runs now; null when it runs nothing yet. */
  prodRelease: string | null;
  /** False when a failed prod rollout cannot restore the previous images on its own; null when unknown. */
  rollbackCompatible: boolean | null;
  /** True when the release declares schema migrations; null while its bundle does not say. */
  hasMigrations: boolean | null;
  /** True when the release declares an end-to-end check for dev; null when its bundle is unknown. */
  verifies: boolean | null;
  /** The check's result on the dev rollout that proved the release; null when it ran none. */
  verification: DeploymentVerification | null;
}
