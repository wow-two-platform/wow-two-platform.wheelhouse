/** Whether Wheelhouse can administer a vault right now. */
export const VaultStatus = {
  Unsealed: 'unsealed',
  Sealed: 'sealed',
  Unreachable: 'unreachable',
} as const;
export type VaultStatus = (typeof VaultStatus)[keyof typeof VaultStatus];

/** A vault and whether Wheelhouse can administer it; its endpoint stays with the inventory editor. */
export interface VaultSummary {
  id: string;
  name: string;
  serverId: string;
  status: VaultStatus;
}

/** A vault namespace, typically one product environment. */
export interface VaultNamespace {
  slug: string;
  name: string;
  createdAtUtc: string;
}

/** A vault's definition as the inventory editor holds it. */
export interface VaultDefinition {
  slug: string;
  name: string;
  /** The slug of the server the vault runs on. */
  server: string;
  url: string;
}

/** A vault's editable definition; the slug is fixed once the vault exists. */
export type SaveVaultRequest = Omit<VaultDefinition, 'slug'> & { slug?: string };
