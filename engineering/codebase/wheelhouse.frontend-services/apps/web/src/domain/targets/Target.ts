/** Which stage of a product a target runs: every `main` release deploys to dev, then is promoted to prod.
 * `test` was retired on 2026-10-17; old records naming it read "test (retired)". */
export const DeploymentEnvironment = {
  Dev: 'dev',
  Prod: 'prod',
} as const;
export type DeploymentEnvironment = (typeof DeploymentEnvironment)[keyof typeof DeploymentEnvironment];

/** The promotion path every product follows — dev, then prod — in the order `DeploymentEnvironment` declares. */
export const EnvironmentOrder: readonly DeploymentEnvironment[] = Object.values(DeploymentEnvironment);

/** An environment's place on the promotion path; an unknown environment sorts after it. */
export function environmentRank(environment: string): number {
  const index = (EnvironmentOrder as readonly string[]).indexOf(environment);
  return index < 0 ? EnvironmentOrder.length : index;
}

/** Where one service's settings file lives on the host. */
export interface TargetSetting {
  service: string;
  path: string;
}

/** A request the runner makes after a rollout, and the status it must answer. */
export interface SmokeCheck {
  service: string;
  path: string;
  status: number;
}

/** The host a named site answers on. */
export interface SiteHost {
  site: string;
  host: string;
}

/** One environment of a product on one server, as the inventory editor holds it. */
export interface InventoryTarget {
  slug: string;
  product: string;
  server: string;
  environment: DeploymentEnvironment;
  network: string;
  root: string;
  settings: TargetSetting[];
  smokeChecks: SmokeCheck[];
  sites: SiteHost[];
}

/** A target's editable definition; the slug is fixed once the target exists. */
export type SaveTargetRequest = Omit<InventoryTarget, 'slug'> & { slug?: string };
