import type { DeploymentTarget } from '@/domain/deployments';
import { ProductStatusRules } from '@/domain/products/ProductStatusRules';
import type { Product } from '@/domain/products';
import { environmentRank } from '@/domain/targets';

/** One product a server hosts, with the environments it runs there. */
export interface HostedProduct {
  readonly slug: string;
  /** The catalog name, or the slug for a product the catalog does not hold. */
  readonly name: string;
  /** The product's environments on the server, in promotion order. */
  readonly environments: readonly string[];
  readonly active: boolean;
}

/**
 * The products one server hosts, by name, read from the targets that place a product environment on it. A server
 * hosts many products; each product environment runs on one server, and its services share it.
 */
export function hostedProductsOf(
  server: string,
  targets: readonly DeploymentTarget[],
  products: readonly Product[],
): HostedProduct[] {
  const environments = new Map<string, string[]>();
  for (const target of targets)
    if (target.serverId === server)
      environments.set(target.product, [...(environments.get(target.product) ?? []), target.environment]);
  return [...environments]
    .map(([slug, names]) => {
      const product = products.find((item) => item.slug === slug);
      return {
        slug,
        name: product?.name ?? slug,
        environments: [...new Set(names)].sort((left, right) => environmentRank(left) - environmentRank(right)),
        active: product ? ProductStatusRules.isActive(product) : true,
      };
    })
    .sort((left, right) => left.name.localeCompare(right.name) || left.slug.localeCompare(right.slug));
}
