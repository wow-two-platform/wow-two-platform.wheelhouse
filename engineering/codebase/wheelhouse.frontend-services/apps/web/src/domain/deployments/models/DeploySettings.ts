/** Where a deploy's render takes a setting's value from. */
export type DeploySettingSource = "derived" | "vault" | "stored" | "default" | "host" | "missing" | "unset";

/** What a setting's value must look like; Wheelhouse checks a value against it on save. */
export type SettingType = "text" | "number" | "boolean" | "choice" | "url" | "cron" | "date" | "email";

/** How Wheelhouse writes a secret value it generates: 32 random bytes as lowercase hex or unpadded base64url. */
export type SecretFormat = "hex" | "base64url";

/** What the console may know about a secret: its version, who changed it and when, its fingerprint and expiry. */
export interface SecretMeta {
  key: string;
  version: number;
  updatedAt: string;
  updatedBy: string;
  /** A keyed fingerprint: equal values of a product show equal fingerprints. */
  fingerprint: string;
  expiresAt: string | null;
  /** How close the expiry is: `expired`, `7d`, `30d`, or null. */
  expiry: "expired" | "7d" | "30d" | null;
}

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
  type: SettingType;
  /** A date key's format, such as `yyyy-MM-dd`. */
  format: string | null;
  /** Whether the owner added the key in the console. */
  adHoc: boolean;
  /** Whether its name reads as a secret: saving it as a plain setting needs a confirmation. */
  looksSecret: boolean;
  /** A vault secret's metadata, never its value. */
  vault: SecretMeta | null;
}

/** One service's settings file on a target. */
export interface DeploySettingsService {
  name: string;
  /** Whether a host settings file is one of its layers; its required keys may come from there. */
  hostFile: boolean;
  keys: DeploySetting[];
}

/** Where an environment's secret values live. */
export interface DeploySettingsVault {
  id: string;
  namespace: string;
  /** Whether values can be read: the built-in vault unsealed, or a standalone vault answering. */
  reachable: boolean;
  mode: "builtin" | "external";
  /** Why the built-in vault is sealed: `missing`, `invalid` or `mismatch`. */
  sealed: string | null;
}

/** A target's scheduled settings apply. */
export interface PendingApply {
  at: string;
  by: string;
}

/** The settings a release takes on a target and where its next deploy takes each from. */
export interface DeploySettings {
  target: string;
  product: string;
  environment: string;
  /** The release's label; null when the target runs none and none was named. */
  release: string | null;
  /** The release bundle the plan read. */
  bundle: string | null;
  firstDeploy: boolean;
  vault: DeploySettingsVault | null;
  services: DeploySettingsService[];
  /** Keys no service declares: those the owner added, and values kept for keys no release names. */
  extra: DeploySetting[];
  /** Keys whose values changed since the target's last deploy rendered them. */
  unapplied: string[];
  pending: PendingApply | null;
  /** Required keys no layer sets, on services without a host file: each blocks a deploy. */
  missing: number;
}

/** A value the owner gives an environment: a secret goes to the vault as a new version, a setting to the store. */
export interface DeploySettingValue {
  key: string;
  value: string;
  secret: boolean;
  /** A secret's note on the change. */
  note?: string;
  /** A secret's expiry: an instant, or null to clear it; left out, the secret keeps its own. */
  expiry?: { at: string | null };
  /** The owner confirmed a key that reads as a secret is a plain setting. */
  confirmPlain?: boolean;
  /** Remove the key's value from the store and the vault. */
  remove?: boolean;
  /** Have Wheelhouse generate a secret's value in this format instead: `value` stays empty, and the value goes to the
   *  vault only, never back to the browser. */
  generate?: SecretFormat;
}

/** One key a save touched. */
export interface SavedSetting {
  key: string;
  secret: boolean;
  removed: boolean;
  version: number | null;
  fingerprint: string | null;
  /** Whether Wheelhouse generated the secret's value. */
  generated?: boolean;
}
