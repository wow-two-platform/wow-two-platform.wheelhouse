import type { Product } from '@/domain/products';
import type { Workspace } from '@/domain/workspaces';

/** The open product as the sidebar's Products destination unfolds it: Workspace › Product. */
export interface ProductTrail {
  /** The product's workspace; null when it has none the console knows. */
  readonly workspace: Workspace | null;
  /** The product's slug, from the address. */
  readonly slug: string;
  /** The product's name, or its slug until the inventory arrives. */
  readonly name: string;
  readonly product: Product | null;
}

/**
 * The trail the sidebar shows under Products while a product page is open; null on any other page. Names come from
 * the inventory; until it arrives the product shows the slug from the address, so the sidebar never waits on a read.
 */
export function productTrailOf(
  productSlug: string | null,
  products: readonly Product[],
  workspaces: readonly Workspace[],
): ProductTrail | null {
  if (!productSlug) return null;
  const product = products.find((item) => item.slug === productSlug) ?? null;
  const workspace = product ? (workspaces.find((item) => item.id === product.workspaceId) ?? null) : null;
  return { workspace, slug: productSlug, name: product?.name ?? productSlug, product };
}
