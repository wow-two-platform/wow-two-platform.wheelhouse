import { InventoryRules } from '@/domain/common';
import type { Product } from '@/domain/products';
import { VpsProvider, type Server } from '@/domain/servers';
import type { InfraJobStatus, JobStep, MintedKey } from './InfraJob';

/** One site placed on a server, under its node name; dev's node adds `-dev`. */
export interface PlacementSite {
  site: string;
  node: string;
}

/** What a placement asks for: a product's dev and prod databases, its site nodes, or both, on one server. */
export interface StartPlacementRequest {
  server: string;
  database: boolean;
  sites: PlacementSite[];
}

/** One placed site in one environment. */
export interface PlacedSite {
  site: string;
  environment: 'dev' | 'prod';
  node: string;
  address: string;
  joined: boolean;
  url?: string | undefined;
  tailnetAddress?: string | null | undefined;
  online?: boolean | undefined;
}

/** A product placed on a server: the job and what it created. */
export interface Placement {
  id: string;
  kind: 'placement';
  serverId: string;
  product: string;
  status: Exclude<InfraJobStatus, 'not_started'>;
  reason: string | null;
  actor: string;
  createdAt: string;
  startedAt: string | null;
  completedAt: string | null;
  request: { serverId: string; product: string; database: boolean; sites: PlacementSite[] };
  facts: { databases: string[]; tailnet?: string | undefined; sites: PlacedSite[] };
  mints: MintedKey[];
  steps: JobStep[];
}

/** The shapes the host accepts for a placement. */
export const PlacementPatterns = {
  site: InventoryRules.slug,
  node: /^[a-z][a-z0-9-]{0,50}[a-z0-9]$/,
} as const;

/** Node names the products host keeps for itself. */
export const RESERVED_NODES: readonly string[] = ['postgres', 'ingress'];

/** The most sites one placement takes. */
export const MAX_PLACED_SITES = 10;

/** Pure rules over a product's placement. */
export const PlacementRules = {
  /** A site's default node name: `<product>-<site>`, such as `ocharo-platform-app`. */
  defaultNode: (product: string, site: string): string => `${product}-${site}`,

  /** Dev's node for a site whose prod node is `node`. */
  devNode: (node: string): string => `${node}-dev`,

  /**
   * The node a row keeps when its site changes: one still at the previous site's default (or empty) follows the new
   * site's default; one the owner typed stays.
   */
  followSite: (product: string, previousSite: string, nextSite: string, node: string): string =>
    !node.trim() || node === PlacementRules.defaultNode(product, previousSite)
      ? nextSite.trim()
        ? PlacementRules.defaultNode(product, nextSite.trim())
        : ''
      : node,

  /** The product's declared site names, in order across its environments; `app` when it declares none. */
  siteNamesOf: (product: Pick<Product, 'environments'>): string[] => {
    const names = [...new Set(product.environments.flatMap((environment) => environment.sites.map((site) => site.name)))];
    return names.length ? names : ['app'];
  },

  /** The rows a new placement starts from: each declared site under its default node. */
  initialSites: (product: Pick<Product, 'slug' | 'environments'>): PlacementSite[] =>
    PlacementRules.siteNamesOf(product).map((site) => ({ site, node: PlacementRules.defaultNode(product.slug, site) })),

  /** The servers a product can be placed on: every one but the local rig. */
  eligibleServers: <TServer extends Pick<Server, 'provider'>>(servers: readonly TServer[]): TServer[] =>
    servers.filter((server) => server.provider !== VpsProvider.Local),

  /** Why a node name is refused, or null: lowercase letters, digits and dashes, not a name the host keeps. */
  nodeProblem: (node: string): string | null =>
    !PlacementPatterns.node.test(node)
      ? 'Use lowercase letters, digits and dashes, starting with a letter.'
      : RESERVED_NODES.includes(node)
        ? `${node} is kept for the host.`
        : null,

  /**
   * The rows whose node clashes with an earlier row's: the same name, or one row's dev node (`<node>-dev`) equal to
   * another's, as the host checks them.
   */
  clashingRows: (sites: readonly PlacementSite[]): number[] => {
    const taken = new Set<string>();
    const clashes: number[] = [];
    sites.forEach((entry, index) => {
      const node = entry.node.trim();
      if (!node) return;
      if (taken.has(node) || taken.has(PlacementRules.devNode(node))) clashes.push(index);
      taken.add(node);
      taken.add(PlacementRules.devNode(node));
    });
    return clashes;
  },

  /** The sites a placement created, by site name, prod before dev. */
  placedSites: (placement: Pick<Placement, 'facts'>): PlacedSite[] =>
    [...placement.facts.sites].sort(
      (left, right) =>
        left.site.localeCompare(right.site) || (left.environment === right.environment ? 0 : left.environment === 'prod' ? -1 : 1),
    ),
} as const;
