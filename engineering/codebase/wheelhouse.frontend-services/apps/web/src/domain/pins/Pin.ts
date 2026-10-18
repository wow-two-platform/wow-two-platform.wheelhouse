/** What a pin keeps one click away. Only products are pinned (2026-10-18); the host still reports a pin's kind. */
export const PinKind = {
  /** A workspace; no longer pinned, and the host converted every one. */
  Workspace: 'workspace',
  Product: 'product',
  /** A service of a product; no longer pinned, and the host turned every one into its product's pin. */
  Service: 'service',
} as const;
export type PinKind = (typeof PinKind)[keyof typeof PinKind];

/** A pinned product, in the owner's custom order. */
export interface Pin {
  id: string;
  kind: PinKind;
  workspaceId: string | null;
  productId: string | null;
  /** Set only on a pin made before pins became products only. */
  service: string | null;
  order: number;
}

/** A product as a pin names it: by its row identifier when the host sends one, else by its slug. */
export interface PinProduct {
  readonly id?: string | undefined;
  readonly slug: string;
}

/** What a pin points at: a product. */
export interface PinSubject {
  readonly kind: typeof PinKind.Product;
  readonly product: PinProduct;
}

/** A new pin. Pinning the same product twice answers the existing pin. */
export interface CreatePinRequest {
  kind: typeof PinKind.Product;
  productId: string;
}
