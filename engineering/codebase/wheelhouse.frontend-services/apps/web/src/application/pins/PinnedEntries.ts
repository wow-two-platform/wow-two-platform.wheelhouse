import { productLocation, type ConsoleLocation } from '@/application/navigation/ConsolePaths';
import { Ordering } from '@/domain/common/Ordering';
import { PinKind, type Pin } from '@/domain/pins/Pin';
import type { Product } from '@/domain/products';
import { ProductStatusRules } from '@/domain/products/ProductStatusRules';

/** One pinned product as the sidebar and the home page show it. */
export interface PinnedEntry {
  readonly pin: Pin;
  readonly label: string;
  /** Where the pin opens; null when its product is gone, so only unpinning remains. */
  readonly location: ConsoleLocation | null;
  readonly productSlug: string | null;
  /** The product itself; null when it is gone. */
  readonly product: Product | null;
  /** False for a pin to an inactive product; it still shows, dimmed. */
  readonly active: boolean;
}

/**
 * Resolves the product pins, in their custom order, against the inventory. A pin of another kind — made before pins
 * became products only — is left out; the host converted every one.
 */
export function resolvePins(pins: readonly Pin[], products: readonly Product[]): PinnedEntry[] {
  return Ordering.sort(pins)
    .filter((pin) => pin.kind === PinKind.Product)
    .map((pin) => {
      const product = products.find((item) => ProductStatusRules.isProduct(item, pin.productId));
      return product
        ? { pin, label: product.name, location: productLocation(product.slug), productSlug: product.slug, product,
            active: ProductStatusRules.isActive(product) }
        : { pin, label: 'Removed product', location: null, productSlug: null, product: null, active: false };
    });
}
