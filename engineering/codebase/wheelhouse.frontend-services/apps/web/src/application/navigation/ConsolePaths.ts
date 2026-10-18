/** The query a product page reads: its environment and what its inspector shows. */
export interface ProductQuery {
  readonly env?: string | null | undefined;
  readonly inspect?: string | null | undefined;
  readonly item?: string | null | undefined;
}

/** A location the router can open: a path, its query and, for a place on a page, its hash. */
export interface ConsoleLocation {
  readonly path: string;
  readonly query: Record<string, string>;
  readonly hash?: string;
}

import { ProductTab, type ProductTabValue } from './ProductTabs';

/** The console's addresses. */
export const ConsolePaths = {
  home: '/',
  /** A workspace's group on the Products page; workspaces have no page of their own. */
  workspace: (slug: string) => `/products${workspaceHash(slug)}`,
  /** A product page on one of its tabs; Overview is the product's own address. */
  product: (slug: string, tab: ProductTabValue | string = ProductTab.Overview) =>
    tab === ProductTab.Overview
      ? `/p/${encodeURIComponent(slug)}`
      : `/p/${encodeURIComponent(slug)}/${encodeURIComponent(tab)}`,
  products: '/products',
  deployments: '/deployments',
  servers: '/servers',
  secrets: '/secrets',
  activity: '/activity',
  keys: '/settings/keys',
  catalog: '/settings/catalog',
} as const;

/** The hash that names a workspace's group on the Products page, which scrolls to it. */
export function workspaceHash(slug: string): string {
  return `#workspace-${encodeURIComponent(slug)}`;
}

/** The workspace a Products page hash names, or null for any other hash. */
export function workspaceOfHash(hash: string | null | undefined): string | null {
  const match = /^#workspace-(.+)$/.exec(hash ?? '');
  if (!match?.[1]) return null;
  try {
    return decodeURIComponent(match[1]);
  } catch {
    return null;
  }
}

/** A workspace's group on the Products page as a router location. */
export function workspaceLocation(slug: string): ConsoleLocation {
  return { path: '/products', query: {}, hash: workspaceHash(slug) };
}

/** Drops absent values so a link never carries an empty parameter. @internal */
function defined(values: Record<string, string | null | undefined>): Record<string, string> {
  return Object.fromEntries(
    Object.entries(values).filter((entry): entry is [string, string] => typeof entry[1] === 'string'),
  );
}

/** A product page, optionally scoped to an environment and an inspected item, on its Overview unless a tab is named. */
export function productLocation(
  slug: string,
  query: ProductQuery = {},
  tab: ProductTabValue | string = ProductTab.Overview,
): ConsoleLocation {
  return {
    path: ConsolePaths.product(slug, tab),
    query: defined({ env: query.env, inspect: query.inspect, item: query.item }),
  };
}

/** The Secrets page opened on one vault, and on one of its namespaces when named. */
export function vaultLocation(vault: string, namespace?: string | null): ConsoleLocation {
  return { path: ConsolePaths.secrets, query: defined({ vault, namespace }) };
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
