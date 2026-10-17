import { ConsolePaths, productLocation, serviceLocation, type ConsoleLocation } from '@/application/navigation/ConsolePaths';
import { Ordering } from '@/domain/common/Ordering';
import { PinKind, type Pin } from '@/domain/pins/Pin';
import type { Product } from '@/domain/products';
import { ProductStatusRules } from '@/domain/products/ProductStatusRules';
import type { Workspace } from '@/domain/workspaces';

/** One pin as the sidebar and the home page show it. */
export interface PinnedEntry {
  readonly pin: Pin;
  readonly label: string;
  /** The product a service pin belongs to; null otherwise. */
  readonly detail: string | null;
  /** Where the pin opens; null when what it pointed at is gone, so only unpinning remains. */
  readonly location: ConsoleLocation | null;
  readonly workspaceSlug: string | null;
  readonly productSlug: string | null;
  readonly service: string | null;
  /** False for a pin to an inactive product or service; it still shows, dimmed. */
  readonly active: boolean;
}

/**
 * Resolves pins, in their custom order, against the inventory. A service pin carries no environment: it opens in the
 * environment last chosen on a product page, else the product's default one.
 */
export function resolvePins(
  pins: readonly Pin[],
  workspaces: readonly Workspace[],
  products: readonly Product[],
  environment: string | null,
): PinnedEntry[] {
  return Ordering.sort(pins).map((pin) => {
    const gone: PinnedEntry = {
      pin, label: 'Removed item', detail: null, location: null, workspaceSlug: null, productSlug: null, service: null,
      active: false,
    };
    if (pin.kind === PinKind.Workspace) {
      const workspace = workspaces.find((item) => item.id === pin.workspaceId);
      return workspace
        ? { ...gone, label: workspace.name, location: { path: ConsolePaths.workspace(workspace.slug), query: {} },
            workspaceSlug: workspace.slug, active: true }
        : gone;
    }
    const product = products.find((item) => ProductStatusRules.isProduct(item, pin.productId));
    if (!product) return gone;
    if (pin.kind === PinKind.Product)
      return { ...gone, label: product.name, location: productLocation(product.slug), productSlug: product.slug,
        active: ProductStatusRules.isActive(product) };
    const service = pin.service ?? '';
    return { ...gone, label: service, detail: product.name, location: serviceLocation(product.slug, service, environment),
      productSlug: product.slug, service,
      active: ProductStatusRules.isActive(product) && ProductStatusRules.isServiceActive(product, service) };
  });
}
