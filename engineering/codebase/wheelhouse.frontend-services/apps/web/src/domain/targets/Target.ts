/** Which stage of a product a target runs. */
export const DeploymentEnvironment = {
  Dev: 'dev',
  Test: 'test',
  Prod: 'prod',
} as const;
export type DeploymentEnvironment = (typeof DeploymentEnvironment)[keyof typeof DeploymentEnvironment];

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
