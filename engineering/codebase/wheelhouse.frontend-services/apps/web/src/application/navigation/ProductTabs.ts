/** The sections of a product page, each at its own address: `/p/{product}` for Overview, `/p/{product}/{tab}` else. */
export const ProductTab = {
  /** Environments, services, comparison and the inspector. */
  Overview: 'overview',
  /** The product's published releases and commit builds. */
  Releases: 'releases',
  /** The product's vault namespace in each environment. */
  Secrets: 'secrets',
  /** The audit trail's entries about the product. */
  Activity: 'activity',
  /** The product's rollouts by day or newest first, under the shared deployments filter. */
  Deployments: 'deployments',
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
  { value: ProductTab.Releases, label: 'Releases' },
  { value: ProductTab.Secrets, label: 'Secrets' },
  { value: ProductTab.Activity, label: 'Activity' },
  { value: ProductTab.Deployments, label: 'Deployments' },
];

/** The tab an address names: a known tab, Overview without one, or null for a segment no tab answers to. */
export function productTabOf(segment: string | null | undefined): ProductTabEntry | null {
  if (!segment) return ProductTabs[0] ?? null;
  return ProductTabs.find((tab) => tab.value === segment) ?? null;
}
