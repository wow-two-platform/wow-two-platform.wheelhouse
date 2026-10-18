import type { Product } from '@/domain/products';
import type { Workspace } from '@/domain/workspaces';
import { ConsolePaths, productLocation, serviceLocation, workspaceLocation, type ConsoleLocation } from './ConsolePaths';
import { ProductTab, productTabOf } from './ProductTabs';

/** Which level of the hierarchy a crumb names. */
export type ScopeLevel = 'home' | 'workspace' | 'product' | 'tab' | 'service' | 'page';

/**
 * One step of the scope the top bar shows: Home › Workspace › Product › Service on a product's Overview,
 * Home › Workspace › Product › Tab on its other tabs, or Home › a tool page.
 */
export interface ScopeCrumb {
  readonly level: ScopeLevel;
  readonly label: string;
  readonly location: ConsoleLocation;
  /** The product a product or service crumb belongs to, for its icon. */
  readonly productSlug?: string;
}

/** What the scope is read from: the route and the inventory the console already holds. */
export interface ScopeInput {
  readonly path: string;
  readonly params: Readonly<{ workspace?: string | null; product?: string | null; tab?: string | null }>;
  readonly query: Readonly<{ env?: string | null; inspect?: string | null; item?: string | null }>;
  /** The route's own title, for a tool page. */
  readonly title: string | null;
  readonly workspaces: readonly Workspace[];
  readonly products: readonly Product[];
}

/** Builds a crumb. @internal */
function crumb(level: ScopeLevel, label: string, location: ConsoleLocation, productSlug?: string): ScopeCrumb {
  return productSlug ? { level, label, location, productSlug } : { level, label, location };
}

/** The workspace crumb: its group on the Products page. @internal */
function workspaceCrumb(workspace: Workspace): ScopeCrumb {
  return crumb('workspace', workspace.name, workspaceLocation(workspace.slug));
}

/**
 * The scope the current page acts on, outermost first. Names come from the inventory; until it arrives a crumb shows
 * the slug from the address, so the bar never waits on a read.
 */
export function scopeOf(input: ScopeInput): ScopeCrumb[] {
  const home = crumb('home', 'Home', { path: ConsolePaths.home, query: {} });
  const { workspace: workspaceSlug, product: productSlug } = input.params;
  if (productSlug) {
    const product = input.products.find((item) => item.slug === productSlug) ?? null;
    const workspace = product ? input.workspaces.find((item) => item.id === product.workspaceId) : undefined;
    const trail = [home, ...(workspace ? [workspaceCrumb(workspace)] : [])];
    const env = input.query.env ?? null;
    trail.push(crumb('product', product?.name ?? productSlug, productLocation(productSlug, { env }), productSlug));
    const tab = input.params.tab ? productTabOf(input.params.tab) : null;
    if (tab && tab.value !== ProductTab.Overview)
      return [...trail, crumb('tab', tab.label, productLocation(productSlug, {}, tab.value), productSlug)];
    if (input.query.inspect === 'service' && input.query.item)
      trail.push(crumb('service', input.query.item, serviceLocation(productSlug, input.query.item, env), productSlug));
    return trail;
  }
  if (workspaceSlug) {
    const workspace = input.workspaces.find((item) => item.slug === workspaceSlug);
    return [
      home,
      workspace ? workspaceCrumb(workspace) : crumb('workspace', workspaceSlug, workspaceLocation(workspaceSlug)),
    ];
  }
  if (input.path === ConsolePaths.home) return [home];
  // One deployment sits under the Deployments page, so its crumb leads back to the list.
  if (input.path.startsWith(`${ConsolePaths.deployments}/`))
    return [
      home,
      crumb('page', 'Deployments', { path: ConsolePaths.deployments, query: {} }),
      crumb('page', input.title ?? 'Deployment', { path: input.path, query: {} }),
    ];
  return [home, crumb('page', input.title ?? input.path, { path: input.path, query: {} })];
}
