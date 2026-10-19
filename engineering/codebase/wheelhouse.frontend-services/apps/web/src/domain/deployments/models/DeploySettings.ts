/** Where a deploy's render takes a setting's value from. */
export type DeploySettingSource = "derived" | "vault" | "stored" | "default" | "host" | "missing" | "unset";

/** One setting a service takes on a target; a secret's value is never sent to the console. */
export interface DeploySetting {
  key: string;
  required: boolean;
  secret: boolean;
  source: DeploySettingSource;
  /** The value of a stored, default or derived setting; null for a secret or an unknown one. */
  value: string | null;
  default: string | null;
  /** What the render derives it from: hosts, proxies, sites or the target's database. */
  derived: "hosts" | "proxies" | "sites" | "database" | null;
  allowed: string[] | null;
}

/** One service's settings file on a target. */
export interface DeploySettingsService {
  name: string;
  /** Whether a host settings file is one of its layers; its required keys may come from there. */
  hostFile: boolean;
  keys: DeploySetting[];
}

/** The settings a release takes on a target and where its next deploy takes each from. */
export interface DeploySettings {
  target: string;
  product: string;
  environment: string;
  release: string;
  firstDeploy: boolean;
  vault: { id: string; namespace: string; reachable: boolean } | null;
  services: DeploySettingsService[];
  /** Required keys no layer sets, on services without a host file: each blocks a deploy. */
  missing: number;
}

/** A value the owner gives an environment: a secret goes to the vault, a setting to the store. */
export interface DeploySettingValue {
  key: string;
  value: string;
  secret: boolean;
}
