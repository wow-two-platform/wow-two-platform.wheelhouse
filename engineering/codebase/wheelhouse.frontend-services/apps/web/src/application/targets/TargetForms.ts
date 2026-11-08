import { z } from "zod";
import { InventoryRules } from "@/domain/common";
import { PlacementRules, type Placement } from "@/domain/infrastructure";
import type { Product, ProductDescriptor } from "@/domain/products";
import type { Server } from "@/domain/servers";
import { SelfDeployRules } from "@/domain/deployments/SelfDeployRules";
import {
  BaselineHost,
  DeploymentEnvironment,
  EnvironmentOrder,
  type InventoryTarget,
  type SaveTargetRequest,
  type SelfTarget,
  type SelfTargetEnvironment,
  type SiteHost,
  type SmokeCheck,
  type TargetSetting,
} from "@/domain/targets";

/** Represents an environment's editable fields; its product is fixed by where the form opens. */
export interface TargetFormModel {
  slug: string;
  environment: DeploymentEnvironment;
  server: string;
  network: string;
  root: string;
  settings: TargetSetting[];
  smokeChecks: SmokeCheck[];
  sites: SiteHost[];
}

/** The rules a site's alias hosts follow; the host enforces the same ones. */
export const SiteAliasRules = {
  /** Aliases one site may carry. */
  max: 20,
  /** Whether text is a lowercase host name: dot-separated labels of letters, digits and inner dashes, 253 characters at most. */
  isHost: (value: string): boolean =>
    value.length > 0 && value.length <= 253 && /^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?(\.[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)+$/.test(value),
  /** The hosts typed one per line or separated by commas or spaces, blanks dropped. */
  parse: (text: string): string[] => text.split(/[\s,]+/).filter(Boolean),
  /** What is wrong with the sites' aliases, by site: an invalid or uppercase alias, more than the maximum, or a host
   * used twice anywhere in the target (every site's host and aliases together). Empty when all is well. */
  errorsOf: (sites: readonly Pick<SiteHost, "site" | "host" | "aliases">[]): Record<string, string> => {
    const errors: Record<string, string> = {};
    const seen = new Map<string, string>();
    for (const item of sites) {
      const host = item.host.trim();
      if (host) seen.set(host, item.site);
    }
    for (const item of sites) {
      const aliases = (item.aliases ?? []).map((alias) => alias.trim()).filter(Boolean);
      const host = item.host.trim();
      if (!aliases.length) continue;
      if (aliases.length > SiteAliasRules.max) {
        errors[item.site] = `At most ${SiteAliasRules.max} aliases.`;
        continue;
      }
      const bad = aliases.find((alias) => !SiteAliasRules.isHost(alias));
      if (bad) {
        errors[item.site] = bad !== bad.toLowerCase() ? `Use lowercase: ${bad}` : `Not a valid host name: ${bad}`;
        continue;
      }
      for (const alias of aliases) {
        if (alias === host) {
          errors[item.site] = `${alias} is already this site's host.`;
          break;
        }
        const owner = seen.get(alias);
        if (owner !== undefined) {
          errors[item.site] = owner === item.site ? `${alias} is listed twice.` : `${alias} is already used by site ${owner}.`;
          break;
        }
        seen.set(alias, item.site);
      }
    }
    return errors;
  },
} as const;

/** Validates what the form can judge alone; the host checks paths, hosts and duplicates and names the field. */
export const targetFormSchema = z.object({
  slug: z.string().trim().regex(InventoryRules.slug, "Start with a lowercase letter; use lowercase letters, digits and dashes."),
  environment: z.enum(DeploymentEnvironment),
  server: z.string().min(1, "Choose a server."),
  network: z.string().trim().min(1, "Enter a network."),
  root: z.string().trim().min(1, "Enter the releases folder."),
  settings: z.array(z.object({ service: z.string().trim(), path: z.string().trim() })),
  smokeChecks: z.array(z.object({ service: z.string().trim(), path: z.string().trim(), status: z.number().int() })),
  sites: z
    .array(z.object({ site: z.string().trim(), host: z.string().trim(), aliases: z.array(z.string().trim()) }))
    .superRefine((sites, context) => {
      const errors = SiteAliasRules.errorsOf(sites);
      sites.forEach((item, index) => {
        const message = errors[item.site];
        if (message) context.addIssue({ code: "custom", message, path: [index, "aliases"] });
      });
    }),
});

/** The slug a new environment of a product suggests. */
export function suggestedTargetSlug(product: string, environment: DeploymentEnvironment): string {
  return `${product}-${environment}`;
}

/** The first environment on the promotion path a product has no target for (dev, then prod); dev when it has them all. */
export function firstFreeEnvironment(taken: readonly string[]): DeploymentEnvironment {
  return EnvironmentOrder.find((environment) => !taken.includes(environment)) ?? DeploymentEnvironment.Dev;
}

/** Creates detached editing state from a target, or a new environment (dev unless told otherwise) on the first server. */
export function targetFormOf(
  target: InventoryTarget | null,
  product: string,
  firstServer: string | null,
  environment: DeploymentEnvironment = DeploymentEnvironment.Dev,
): TargetFormModel {
  return {
    slug: target?.slug ?? suggestedTargetSlug(product, environment),
    environment: target?.environment ?? environment,
    server: target?.server ?? firstServer ?? "",
    network: target?.network ?? "platform",
    root: target?.root ?? "/srv/wheelhouse",
    settings: target?.settings.map((setting) => ({ ...setting })) ?? [],
    smokeChecks: target?.smokeChecks.map((check) => ({ ...check })) ?? [],
    sites: target?.sites.map((site) => ({ ...site, aliases: [...(site.aliases ?? [])] })) ?? [],
  };
}

/** The request a submitted form sends; the slug goes only with a new environment. */
export function targetRequestOf(values: TargetFormModel, product: string, isNew: boolean): SaveTargetRequest {
  return {
    ...(isNew ? { slug: values.slug } : {}),
    product,
    server: values.server,
    environment: values.environment,
    network: values.network,
    root: values.root,
    settings: values.settings,
    smokeChecks: values.smokeChecks,
    sites: values.sites,
  };
}

/** A site the product is known to publish, from its environments' last rollouts. */
export interface KnownSite {
  name: string;
  exposure: "public" | "private";
  /** The front door, whose baseline host carries no site name. */
  primary: boolean;
}

/**
 * The sites a product publishes, by name. Its deploy.yml, once read, is the source: each declared site with its exposure
 * and front door. Without it, the sites its environments' last rollouts published: public when any environment
 * publishes it publicly, the front door the site a rollout marked primary, else the first by name.
 */
export function knownSitesOf(
  product: Pick<Product, "environments"> | null | undefined,
  descriptor?: Pick<ProductDescriptor, "problem" | "sites"> | null,
): KnownSite[] {
  if (descriptor && !descriptor.problem)
    return descriptor.sites
      .map((site) => ({ name: site.name, exposure: site.exposure, primary: site.primary }))
      .sort((left, right) => left.name.localeCompare(right.name));
  const sites = new Map<string, KnownSite>();
  for (const environment of product?.environments ?? [])
    for (const site of environment.sites) {
      const known = sites.get(site.name) ?? { name: site.name, exposure: site.exposure, primary: false };
      if (site.exposure === "public") known.exposure = "public";
      known.primary ||= site.primary === true;
      sites.set(site.name, known);
    }
  const list = [...sites.values()].sort((left, right) => left.name.localeCompare(right.name));
  const first = list[0];
  if (first && !list.some((site) => site.primary)) first.primary = true;
  return list;
}

/** The public address of the server an environment runs on, if enrollment or kit refresh recorded one. */
export function publicAddressOf(servers: readonly Pick<Server, "slug" | "ingress">[], server: string): string | null {
  return servers.find((item) => item.slug === server)?.ingress?.publicAddress ?? null;
}

/** One site of the environment form: what the product declares and the host it answers on, both read-only. */
export interface SiteHostRow {
  site: string;
  /** Null for a stored host of a site the product no longer declares. */
  exposure: "public" | "private" | null;
  /** The host the form sends for the site; empty when it has none (a private site on its node, or no address yet). */
  host: string;
  /** A private site's tailnet node in this environment. */
  node: string | null;
  /** The extra hosts the site also answers on, as stored. */
  aliases: string[];
  /** Why the row reads as it does, or null for a host derived as usual. */
  note: string | null;
}

/** What the site rows are derived for. */
export interface SiteHostContext {
  readonly product: string;
  readonly environment: DeploymentEnvironment;
  /** The chosen server's public address, if one is recorded. */
  readonly address: string | null;
  /** A private site's tailnet node in this environment on the chosen server. */
  readonly node: (site: string) => string;
}

/**
 * The hosts a target keeps for the environment and server the form shows. A target keeps every host it stores; moving
 * it to another environment or server leaves behind its baseline hosts, which belong to the pair they were derived for
 * (decision 68), so they follow. A bound host stays.
 */
export function storedHostsOf(
  target: Pick<InventoryTarget, "sites" | "environment" | "server"> | null,
  environment: DeploymentEnvironment,
  server: string,
): SiteHost[] {
  if (!target) return [];
  const moved = target.environment !== environment || target.server !== server;
  return target.sites.filter((site) => site.host.trim() && !(moved && BaselineHost.isBaseline(site.host)));
}

/**
 * One row per site the product declares, in the declared order, none typed (decisions 47, 68): the stored host when the
 * target has one, else a public site's baseline host on the chosen server, else, for a private site, its tailnet node.
 * A stored host that is not the derived one stays, with a note, so saving changes nothing unseen; a stored host of a
 * site the product no longer declares stays last. A public site without an address yet has no host: the deploy fills it.
 */
export function siteRowsOf(
  known: readonly KnownSite[],
  stored: readonly SiteHost[],
  context: SiteHostContext,
): SiteHostRow[] {
  const kept = new Map<string, string>();
  const aliasesOf = new Map<string, string[]>();
  for (const item of stored) {
    const site = item.site.trim();
    const host = item.host.trim();
    if (site && host && !kept.has(site)) {
      kept.set(site, host);
      aliasesOf.set(site, [...(item.aliases ?? [])]);
    }
  }
  const rows = known.map((site): SiteHostRow => {
    const host = kept.get(site.name) ?? "";
    const aliases = aliasesOf.get(site.name) ?? [];
    kept.delete(site.name);
    if (site.exposure === "private") {
      const node = context.node(site.name);
      return { site: site.name, exposure: "private", host, node, aliases, note: host ? "Private; stored host" : "Private; tailnet node" };
    }
    const derived = BaselineHost.of(context.product, site.name, context.environment, context.address, site.primary);
    if (host)
      return { site: site.name, exposure: "public", host, node: null, aliases, note: host === derived ? null : derived ? `Stored; derived: ${derived}` : "Stored" };
    return derived
      ? { site: site.name, exposure: "public", host: derived, node: null, aliases, note: null }
      : { site: site.name, exposure: "public", host: "", node: null, aliases, note: "Set once the server has a public address" };
  });
  for (const [site, host] of kept)
    rows.push({ site, exposure: null, host, node: null, aliases: aliasesOf.get(site) ?? [], note: "Stored; not declared by the product" });
  return rows;
}

/** The hosts a form with these rows sends: every row that has one, with its aliases (typed ones replace the stored). */
export function siteHostsOf(rows: readonly SiteHostRow[], typed: Readonly<Record<string, string[]>> = {}): SiteHost[] {
  return rows.filter((row) => row.host).map((row) => ({ site: row.site, host: row.host, aliases: typed[row.site] ?? row.aliases }));
}

/**
 * A private site's tailnet node in an environment on a server: the node the server registered for it, else
 * `<product>-<site>`, with `-dev` added for dev (the placement dialog names it the same way).
 */
export function tailnetNodeOf(
  product: string,
  site: string,
  server: string,
  environment: DeploymentEnvironment,
  placements: readonly Pick<Placement, "serverId" | "facts">[] = [],
): string {
  const node = PlacementRules.nodeFor(product, site, server, placements);
  return environment === DeploymentEnvironment.Prod ? node : PlacementRules.devNode(node);
}

/**
 * The servers an environment of the product can run on: every server, except that Wheelhouse's own environments run
 * only on the control host, the server the consoles reach at a private address (decision 64).
 */
export function serverChoicesOf<T extends Pick<Server, "controlHost">>(product: string, servers: readonly T[]): T[] {
  return SelfDeployRules.isSelfProduct(product) ? servers.filter((server) => server.controlHost === true) : [...servers];
}

/** What Wheelhouse derives for one of its own environments, once the console's read has it. */
export function selfEnvironmentOf(
  self: SelfTarget | null | undefined,
  environment: DeploymentEnvironment,
): SelfTargetEnvironment | null {
  return self?.environments.find((item) => item.environment === environment) ?? null;
}

/** The form with what Wheelhouse derives for its own environment: network, folder and settings file; the rest stays. */
export function withSelfDefinition(values: TargetFormModel, derived: SelfTargetEnvironment): TargetFormModel {
  return {
    ...values,
    network: derived.network,
    root: derived.root,
    settings: derived.settings.map((setting) => ({ ...setting })),
  };
}

/**
 * The site rows of one of Wheelhouse's own environments: the console site on this console's own hostname, which dev
 * shares and answers on port 8443. While the console names no hostname, the row has none and says so; before the read
 * arrives there is no row.
 */
export function selfSiteRowsOf(
  derived: SelfTargetEnvironment | null,
  environment: DeploymentEnvironment,
  site = "console",
): SiteHostRow[] {
  if (!derived) return [];
  const port = environment === DeploymentEnvironment.Dev ? "; dev on port 8443" : "";
  const rows = derived.sites.map((item): SiteHostRow => ({
    site: item.site,
    exposure: "private",
    host: item.host,
    node: null,
    aliases: [...(item.aliases ?? [])],
    note: `This console's hostname${port}`,
  }));
  return rows.length
    ? rows
    : [{ site, exposure: "private", host: "", node: null, aliases: [], note: "This console's settings name no hostname" }];
}
