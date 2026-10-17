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
}
