/** One version in a secret's history; never its value. */
export interface SecretVersion {
  version: number;
  createdBy: string;
  createdAt: string;
  fingerprint: string;
  note: string | null;
  /** Where the value came from when not typed: a restore or an import. */
  origin: string | null;
  current: boolean;
}

/** A secret's history in one environment, newest version first. */
export interface SecretHistory {
  target: string;
  key: string;
  removed: boolean;
  expiresAt: string | null;
  versions: SecretVersion[];
}

/** An apply of edited settings: submitted now, or scheduled. */
export interface SettingsApply {
  target: string;
  mode: "now" | "scheduled";
  at: string | null;
  by: string;
  deployment: string | null;
  release: string | null;
}

/** The built-in vault's state for the console; never a key or a value. */
export interface BuiltInVaultStatus {
  mode: "builtin" | "external";
  state: "unsealed" | "sealed";
  /** `missing`, `invalid` or `mismatch`; null when unsealed. */
  reason: string | null;
  keyFile: string;
  products: number;
  secrets: number;
  expiring: number;
  expired: number;
}

/** A secret lapsing soon or lapsed. */
export interface VaultExpiry {
  product: string;
  environment: string;
  target: string | null;
  key: string;
  expiresAt: string;
  level: "expired" | "7d" | "30d";
}

/** What the built-in vault needs the owner for. */
export interface BuiltInVaultIssues {
  state: "unsealed" | "sealed";
  reason: string | null;
  expiries: VaultExpiry[];
}

/** A full decryption check, in counts. */
export interface VaultVerification {
  state: "unsealed" | "sealed";
  canary: boolean;
  products: number;
  secrets: number;
  versions: number;
  failed: number;
  failures: string[];
}

/** What an import from a standalone vault did, in counts. */
export interface VaultImport {
  source: string;
  namespaces: number;
  unmatched: string[];
  secrets: number;
  versions: number;
  skipped: number;
  verified: number;
  failed: number;
  failures: string[];
}

/** A key a deploy's render changed against the target's last good render. */
export interface SettingChange {
  key: string;
  kind: "setting" | "secret";
  change: "added" | "changed" | "removed";
  from: number | null;
  to: number | null;
}

/** A key the owner adds to a product. */
export interface SettingKeyRequest {
  type: string;
  secret: boolean;
  required: boolean;
  format?: string;
  choices?: string[];
  note?: string;
  confirmPlain?: boolean;
}
