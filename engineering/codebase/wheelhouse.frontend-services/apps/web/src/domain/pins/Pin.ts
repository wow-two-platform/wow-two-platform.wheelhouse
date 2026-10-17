/** What a pin keeps one click away. */
export const PinKind = {
  Workspace: 'workspace',
  Product: 'product',
  /** A service of a product; one pin covers every environment, and opens the one selected. */
  Service: 'service',
} as const;
export type PinKind = (typeof PinKind)[keyof typeof PinKind];

/** A workspace, product or service the operator pinned, in the custom order. */
export interface Pin {
  id: string;
  kind: PinKind;
  workspaceId: string | null;
  productId: string | null;
  /** The service's key in the product's deployment; set for a service pin only. */
  service: string | null;
  order: number;
}

/** A product as a pin names it: by its row identifier when the host sends one, else by its slug. */
export interface PinProduct {
  readonly id?: string | undefined;
  readonly slug: string;
}

/** What a pin points at. */
export type PinSubject =
  | { readonly kind: typeof PinKind.Workspace; readonly workspaceId: string }
  | { readonly kind: typeof PinKind.Product; readonly product: PinProduct }
  | { readonly kind: typeof PinKind.Service; readonly product: PinProduct; readonly service: string };

/** A new pin. Pinning the same thing twice answers the existing pin. */
export interface CreatePinRequest {
  kind: PinKind;
  workspaceId?: string;
  productId?: string;
  service?: string;
}
