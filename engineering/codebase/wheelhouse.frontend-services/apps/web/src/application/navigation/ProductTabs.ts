/** The sections of a product page, each at its own address: `/p/{product}` for Overview, `/p/{product}/{tab}` else. */
export const ProductTab = {
  /** Environments, services, comparison and the inspector. */
  Overview: 'overview',
  /** The product's rollouts by day or newest first, under the shared deployments filter, and the releases and commit
   * builds it can deploy. */
  Deployments: 'deployments',
  /** The settings each environment renders: declared and added keys, typed, saved field by field. */
  Settings: 'settings',
  /** Each environment's secrets in the built-in vault: versions, history, expiry. */
  Secrets: 'secrets',
  /** The audit trail's entries about the product. */
  Activity: 'activity',
} as const;
export type ProductTabValue = (typeof ProductTab)[keyof typeof ProductTab];

/** One tab of a product page: its value, the path segment that addresses it, and its label. */
export interface ProductTabEntry {
  readonly value: ProductTabValue;
  readonly label: string;
}

/**
 * The product page's tabs in the order they show. A new tab appends an entry here and a panel in the product page's
 * panel table; Overview always leads, since a bare product address opens it.
 */
export const ProductTabs: readonly ProductTabEntry[] = [
  { value: ProductTab.Overview, label: 'Overview' },
  { value: ProductTab.Deployments, label: 'Deployments' },
  { value: ProductTab.Settings, label: 'Settings' },
  { value: ProductTab.Secrets, label: 'Secrets' },
  { value: ProductTab.Activity, label: 'Activity' },
];

/** Tabs that moved: an old address's segment and the tab that holds it now. Releases joined Deployments (2026-10-19). */
export const MovedProductTabs: Readonly<Record<string, ProductTabValue>> = {
  releases: ProductTab.Deployments,
};

/** The tab an address names: a known tab, Overview without one, or null for a segment no tab answers to. */
export function productTabOf(segment: string | null | undefined): ProductTabEntry | null {
  if (!segment) return ProductTabs[0] ?? null;
  return ProductTabs.find((tab) => tab.value === segment) ?? null;
}
