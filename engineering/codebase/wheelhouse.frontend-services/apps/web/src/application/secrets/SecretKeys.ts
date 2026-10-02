/** Query keys for vault metadata; changes invalidate through these. */
export const SecretKeys = {
  vaults: ["vaults"] as const,
  definitions: ["vaults", "definitions"] as const,
  hygiene: (vault: string) => ["vaults", vault, "hygiene"] as const,
  namespaces: (vault: string) => ["vaults", vault, "namespaces"] as const,
  secrets: (vault: string, ns: string) =>
    ["vaults", vault, "secrets", ns] as const,
  tokens: (vault: string, ns: string) =>
    ["vaults", vault, "tokens", ns] as const,
};
