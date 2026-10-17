import { ProductStatus, type Product } from './Product';

/** Pure rules for active and inactive products and services. */
export const ProductStatusRules = {
  /** Whether the product is in use. */
  isActive: (product: Pick<Product, 'status'>): boolean => product.status === ProductStatus.Active,

  /** Whether a service of the product is in use; a service the product does not list as inactive is active. */
  isServiceActive: (product: Pick<Product, 'inactiveServices'> | null | undefined, service: string): boolean =>
    !(product?.inactiveServices ?? []).includes(service),

  /** The items to show: all of them while inactive ones are shown, else only the active ones. */
  visible: <T>(items: readonly T[], isActive: (item: T) => boolean, showInactive: boolean): T[] =>
    showInactive ? [...items] : items.filter(isActive),

  /** The product with a new status, for an optimistic update. */
  withStatus: <T extends Product>(product: T, status: ProductStatus): T =>
    product.status === status ? product : { ...product, status },

  /** The product with one service's status changed, for an optimistic update; the list stays sorted. */
  withServiceStatus: <T extends Product>(product: T, service: string, status: ProductStatus): T => {
    const others = product.inactiveServices.filter((name) => name !== service);
    const inactiveServices = status === ProductStatus.Inactive ? [...others, service].sort() : others;
    return { ...product, inactiveServices };
  },

  /** The product moved to another workspace, for an optimistic update. */
  withWorkspace: <T extends Product>(product: T, workspaceId: string | null): T =>
    product.workspaceId === workspaceId ? product : { ...product, workspaceId },

  /** Whether a reference — a row identifier or a slug — names this product. */
  isProduct: (product: Pick<Product, 'id' | 'slug'>, reference: string | null | undefined): boolean =>
    reference != null && (reference === product.slug || (product.id != null && reference === product.id)),

  /** The reference pins and other records use for the product: its row identifier when the host sends one. */
  referenceOf: (product: Pick<Product, 'id' | 'slug'>): string => product.id ?? product.slug,
} as const;
