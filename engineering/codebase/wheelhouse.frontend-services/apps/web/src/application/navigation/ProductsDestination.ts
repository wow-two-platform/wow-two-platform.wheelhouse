import { ConsolePaths } from './ConsolePaths';
import { primaryDestinationOf } from './PrimaryNavigation';
import { ProductTab } from './ProductTabs';

/** The product page last opened, as the Products destination remembers it. */
export interface RememberedProduct {
  readonly slug: string;
  readonly tab: string | null;
}

/**
 * Where the primary Products destination leads, as a tab bar's tab does: from outside Products it returns to the
 * product page last opened (on its tab), or the products grid before any; from inside Products — the grid or a product
 * page — it leads up to the grid. "All products" always leads to the grid.
 */
export function productsDestination(currentPath: string, remembered: RememberedProduct | null): string {
  if (!remembered || primaryDestinationOf(currentPath) === ConsolePaths.products) return ConsolePaths.products;
  return ConsolePaths.product(remembered.slug, remembered.tab ?? ProductTab.Overview);
}
