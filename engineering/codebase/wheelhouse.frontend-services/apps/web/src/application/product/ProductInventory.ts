import type { DeploymentTarget } from '@/domain/deployments';
import type { Product } from '@/domain/products';

/** Represents one product with its targets, keyed by its catalog slug. */
export interface InventoryProduct {
  /** The product's slug, which its targets name as their product. */
  readonly key: string;
  /** The catalog entry, or null for targets whose product the catalog did not return. */
  readonly product: Product | null;
  readonly targets: readonly DeploymentTarget[];
}

/** Represents the complete product inventory, including targets outside the catalog. */
export interface ProductInventory {
  readonly products: readonly InventoryProduct[];
}

/** Builds from successfully loaded reads: catalog products in their order, then any uncataloged target products. */
export function buildProductInventory(
  products: readonly Product[],
  targets: readonly DeploymentTarget[],
): ProductInventory {
  const groups = new Map<string, DeploymentTarget[]>();
  for (const target of targets) {
    const group = groups.get(target.product) ?? [];
    group.push(target);
    groups.set(target.product, group);
  }

  const entries: InventoryProduct[] = products.map((product) => ({
    key: product.slug,
    product,
    targets: groups.get(product.slug) ?? [],
  }));
  const cataloged = new Set(products.map((product) => product.slug));
  for (const [slug, productTargets] of groups) {
    if (!cataloged.has(slug)) entries.push({ key: slug, product: null, targets: productTargets });
  }
  return { products: entries };
}
