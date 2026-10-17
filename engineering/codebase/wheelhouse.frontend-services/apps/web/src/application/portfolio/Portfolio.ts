import { Ordering } from '@/domain/common/Ordering';
import { ProductStatusRules } from '@/domain/products/ProductStatusRules';
import type { Product } from '@/domain/products';
import type { Workspace } from '@/domain/workspaces';

/** One workspace's products as the home page, the sidebar and the phone's product sheet list them. */
export interface PortfolioGroup {
  /** The workspace; null for the products whose workspace the console does not know. */
  readonly workspace: Workspace | null;
  /** The products to show, by name: the active ones, or all of them while inactive ones are shown. */
  readonly products: readonly Product[];
  /** Every product in the workspace, shown or not. */
  readonly total: number;
  /** The workspace's active products. */
  readonly active: number;
}

/** Sorts products by name, then slug, so a list keeps its place between reads. @internal */
function byName(left: Product, right: Product): number {
  return left.name.localeCompare(right.name) || left.slug.localeCompare(right.slug);
}

/**
 * Groups the portfolio by workspace, in the workspaces' custom order. A workspace without products still shows, so it
 * can be filled; products naming no known workspace close the list under a group without one.
 */
export function groupPortfolio(
  workspaces: readonly Workspace[],
  products: readonly Product[],
  showInactive: boolean,
): PortfolioGroup[] {
  const known = new Set(workspaces.map((workspace) => workspace.id));
  const group = (workspace: Workspace | null, members: Product[]): PortfolioGroup => {
    const sorted = [...members].sort(byName);
    return {
      workspace,
      products: ProductStatusRules.visible(sorted, ProductStatusRules.isActive, showInactive),
      total: sorted.length,
      active: sorted.filter(ProductStatusRules.isActive).length,
    };
  };
  const groups = Ordering.sort(workspaces).map((workspace) =>
    group(workspace, products.filter((product) => product.workspaceId === workspace.id)),
  );
  const orphans = products.filter((product) => product.workspaceId === null || !known.has(product.workspaceId));
  return orphans.length ? [...groups, group(null, orphans)] : groups;
}

/** The workspace a product belongs to, if the console knows it. */
export function workspaceOf(workspaces: readonly Workspace[], product: Product | null | undefined): Workspace | null {
  return (product && workspaces.find((workspace) => workspace.id === product.workspaceId)) || null;
}
