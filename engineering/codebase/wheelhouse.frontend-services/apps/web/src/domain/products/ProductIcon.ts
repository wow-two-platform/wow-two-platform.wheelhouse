/** The surface an icon is drawn for: `light` has dark ink, `dark` has light ink. */
export const IconTheme = { Light: 'light', Dark: 'dark' } as const;
export type IconTheme = (typeof IconTheme)[keyof typeof IconTheme];

/** How much of the identity an icon shows: the symbol with its wordmark, or the symbol alone. */
export const IconForm = { Full: 'full', Short: 'short' } as const;
export type IconForm = (typeof IconForm)[keyof typeof IconForm];

/** The ink of an icon: the brand's colors, or one flat ink. */
export const IconColor = { Colored: 'colored', Monochrome: 'monochrome' } as const;
export type IconColor = (typeof IconColor)[keyof typeof IconColor];

/** Which icon of a product is wanted; absent values ask for the symbol in its brand colors on a light surface. */
export interface ProductIconOptions {
  /** A service of the product, whose own icon is preferred; the product's icon answers when it has none. */
  service?: string | null | undefined;
  theme?: IconTheme | undefined;
  form?: IconForm | undefined;
  color?: IconColor | undefined;
}

/**
 * The console path of a product's or service's icon. The host resolves the file from the product's repository by the
 * wow-two product icons convention, so the path names the variant, never a file.
 */
export function productIconUrl(slug: string, options: ProductIconOptions = {}): string {
  const { service, theme = IconTheme.Light, form = IconForm.Short, color = IconColor.Colored } = options;
  const scope = service ? `/services/${encodeURIComponent(service)}` : '';
  return `/api/products/${encodeURIComponent(slug)}${scope}/icon?theme=${theme}&form=${form}&color=${color}`;
}

/** The monogram a product shows when no icon resolves: the first letter of its name, or `?` for a blank one. */
export function monogramOf(name: string): string {
  return name.trim().slice(0, 1).toUpperCase() || '?';
}

/** The hue, in degrees, of a product's monogram tint; the same name always lands on the same one. */
export function monogramHue(name: string): number {
  return [...name].reduce((sum, character) => (sum * 31 + character.charCodeAt(0)) % 360, 17);
}
