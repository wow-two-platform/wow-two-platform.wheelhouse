/** What the control plane deploys of one product on its own. */
export interface ProductAutomation {
  readonly product: string;
  /** Each new published release deploys to the product's dev targets. On unless the owner turned it off. */
  readonly autoDeployDev: boolean;
  /** A release that passed its dev watch window is promoted to prod. Off unless the owner turned it on. */
  readonly autoPromote: boolean;
  /** The product's own CI deploys it (Wheelhouse itself), so the control plane never does. */
  readonly ciDeployed: boolean;
}

/** Why an automatic deploy waits: a new release for dev, or a promotion after the dev watch. */
export const DeploymentCause = { AutoDev: 'auto_dev', AutoPromote: 'auto_promote' } as const;
export type DeploymentCause = (typeof DeploymentCause)[keyof typeof DeploymentCause];

/** An automatic deploy waiting for its target; one per target, the latest release wins. */
export interface QueuedDeployment {
  readonly targetId: string;
  readonly product: string;
  readonly environment: string;
  readonly release: string;
  readonly bundleId: string;
  readonly publishedAt: string | null;
  readonly cause: DeploymentCause;
  readonly queuedAt: string;
}

/** Every product's automation choices and the automatic deploys waiting. */
export interface DeploymentAutomation {
  readonly products: readonly ProductAutomation[];
  readonly queue: readonly QueuedDeployment[];
}

/** The choices a product takes when the host names none: auto-deploy to dev on, auto-promote off. */
export const DefaultAutomation = (product: string): ProductAutomation => ({
  product,
  autoDeployDev: true,
  autoPromote: false,
  ciDeployed: false,
});
