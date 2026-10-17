import type { PortfolioGroup } from '@/application/portfolio/Portfolio';
import type { EntityTree } from '@/domain/deployments';
import type { Product } from '@/domain/products';

/** A product's service keys: what its manifest names and what its release ships, sorted. @internal */
function servicesOf(product: Product, showInactive: boolean): string[] {
  const keys = new Set([
    ...product.identity.services.map((item) => item.service),
    ...(product.release?.images.map((item) => item.service) ?? []),
  ]);
  return [...keys].filter((key) => showInactive || !product.inactiveServices.includes(key)).sort();
}

/**
 * The entity picker's tree from the portfolio groups: each workspace with the products it shows and their services.
 * Inactive services stay out until the operator shows inactive ones, as everywhere else.
 */
export function buildEntityTree(groups: readonly PortfolioGroup[], showInactive: boolean): EntityTree {
  return groups
    .filter((group) => group.workspace !== null)
    .map((group) => ({
      slug: group.workspace!.slug,
      name: group.workspace!.name,
      products: group.products.map((product) => ({
        slug: product.slug,
        name: product.name,
        services: servicesOf(product, showInactive),
      })),
    }));
}
