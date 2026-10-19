/** Query keys for settings and the built-in vault; a save invalidates the target's plans, whatever the release. */
export const SettingsKeys = {
  /** Every plan of a target: the deploy dialog's per release, and the editors' for the running one. */
  of: (target: string) => ["deployments", "settings", target] as const,
  /** The editors' plan: the release the target runs. */
  running: (target: string) => ["deployments", "settings", target, "running"] as const,
  /** A secret's versions in a target's environment. */
  history: (target: string, key: string) => ["deployments", "settings", target, "history", key] as const,
  /** The built-in vault's state and issues. */
  vault: ["vault"] as const,
  vaultStatus: ["vault", "status"] as const,
  vaultIssues: ["vault", "issues"] as const,
};
