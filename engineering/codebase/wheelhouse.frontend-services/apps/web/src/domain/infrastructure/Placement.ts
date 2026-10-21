import { InventoryRules } from '@/domain/common';
import { ProductDescriptorRules, type ProductDescriptor } from '@/domain/products';
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

/** What a product's deploy.yml calls for on a server. */
export interface PlacementPlan {
  /** The services that need PostgreSQL; any one gives the product a dev and a prod database. */
  databases: string[];
  /** The private sites, each needing a tailnet node per environment. */
  sites: string[];
  /** The public sites, which answer on the server's baseline hosts and need nothing placed. */
  publicSites: string[];
}

/** Pure rules over a product's placement. */
export const PlacementRules = {
  /** A site's default node name: `<product>-<site>`, such as `ocharo-platform-admin`. */
  defaultNode: (product: string, site: string): string => `${product}-${site}`,

  /** Dev's node for a site whose prod node is `node`. */
  devNode: (node: string): string => `${node}-dev`,

  /** What the descriptor calls for: databases when a service needs PostgreSQL, a node per private site. */
  planOf: (descriptor: Pick<ProductDescriptor, 'services' | 'sites'>): PlacementPlan => ({
    databases: ProductDescriptorRules.postgresServices(descriptor),
    sites: ProductDescriptorRules.privateSites(descriptor),
    publicSites: descriptor.sites.filter((site) => site.exposure === 'public').map((site) => site.name),
  }),

  /** Whether a plan holds anything to place. */
  hasWork: (plan: PlacementPlan): boolean => plan.databases.length > 0 || plan.sites.length > 0,

  /**
   * The prod node a site is placed under on a server: the one the host registered for it in an earlier placement there,
   * newest first, since the host keeps a site's node; else `<product>-<site>`.
   */
  nodeFor: (
    product: string,
    site: string,
    server: string,
    placements: readonly Pick<Placement, 'serverId' | 'facts'>[] = [],
  ): string =>
    placements
      .filter((placement) => placement.serverId === server)
      .flatMap((placement) => placement.facts.sites)
      .find((placed) => placed.site === site && placed.environment === 'prod')?.node ??
    PlacementRules.defaultNode(product, site),

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

  /** The sites a placement created, by site name, prod before dev. */
  placedSites: (placement: Pick<Placement, 'facts'>): PlacedSite[] =>
    [...placement.facts.sites].sort(
      (left, right) =>
        left.site.localeCompare(right.site) || (left.environment === right.environment ? 0 : left.environment === 'prod' ? -1 : 1),
    ),
} as const;
