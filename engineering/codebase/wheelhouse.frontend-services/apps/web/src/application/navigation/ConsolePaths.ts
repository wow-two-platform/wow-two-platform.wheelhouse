/** The query a product page reads: its environment and what its inspector shows. */
export interface ProductQuery {
  readonly env?: string | null | undefined;
  readonly inspect?: string | null | undefined;
  readonly item?: string | null | undefined;
}

/** A location the router can open: a path and its query. */
export interface ConsoleLocation {
  readonly path: string;
  readonly query: Record<string, string>;
}

/** The console's addresses. */
export const ConsolePaths = {
  home: '/',
  workspace: (slug: string) => `/w/${encodeURIComponent(slug)}`,
  product: (slug: string) => `/p/${encodeURIComponent(slug)}`,
  products: '/products',
  deployments: '/deployments',
  servers: '/servers',
  secrets: '/secrets',
  activity: '/activity',
  keys: '/settings/keys',
  catalog: '/settings/catalog',
} as const;

/** Drops absent values so a link never carries an empty parameter. @internal */
function defined(values: Record<string, string | null | undefined>): Record<string, string> {
  return Object.fromEntries(
    Object.entries(values).filter((entry): entry is [string, string] => typeof entry[1] === 'string'),
  );
}

/** A product page, optionally scoped to an environment and an inspected item. */
export function productLocation(slug: string, query: ProductQuery = {}): ConsoleLocation {
  return { path: ConsolePaths.product(slug), query: defined({ env: query.env, inspect: query.inspect, item: query.item }) };
}

/** A service of a product in the given environment, or the product's default one when none is chosen. */
export function serviceLocation(slug: string, service: string, env: string | null | undefined): ConsoleLocation {
  return productLocation(slug, { env, inspect: 'service', item: service });
}

/** Reads one query value the way vue-router hands it over: a string, an array or nothing. @internal */
function first(value: unknown): string | null {
  const item = Array.isArray(value) ? value[0] : value;
  return typeof item === 'string' ? item : null;
}

/**
 * Where an old Workspace link (`/?product=x&target=…&inspect=…&item=…`) now leads: the product's own page with the
 * other parameters kept. The page turns `target` into its `env` once the product's targets are read. Null when the
 * link names no product.
 */
export function legacyProductRedirect(query: Readonly<Record<string, unknown>>): ConsoleLocation | null {
  const product = first(query['product']);
  if (product === null || product === '') return null;
  const rest = Object.fromEntries(
    Object.entries(query)
      .filter(([key]) => key !== 'product')
      .map(([key, value]) => [key, first(value)] as const),
  );
  return { path: ConsolePaths.product(product), query: defined(rest) };
}
