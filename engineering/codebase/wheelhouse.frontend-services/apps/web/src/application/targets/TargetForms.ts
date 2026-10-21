import { z } from "zod";
import { InventoryRules } from "@/domain/common";
import type { Product, ProductDescriptor } from "@/domain/products";
import type { Server } from "@/domain/servers";
import {
  BaselineHost,
  DeploymentEnvironment,
  type InventoryTarget,
  type SaveTargetRequest,
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

/** Validates what the form can judge alone; the host checks paths, hosts and duplicates and names the field. */
export const targetFormSchema = z.object({
  slug: z.string().trim().regex(InventoryRules.slug, "Start with a lowercase letter; use lowercase letters, digits and dashes."),
  environment: z.enum(DeploymentEnvironment),
  server: z.string().min(1, "Choose a server."),
  network: z.string().trim().min(1, "Enter a network."),
  root: z.string().trim().min(1, "Enter the releases folder."),
  settings: z.array(z.object({ service: z.string().trim(), path: z.string().trim() })),
  smokeChecks: z.array(z.object({ service: z.string().trim(), path: z.string().trim(), status: z.number().int() })),
  sites: z.array(z.object({ site: z.string().trim(), host: z.string().trim() })),
});

/** The slug a new environment of a product suggests. */
export function suggestedTargetSlug(product: string, environment: DeploymentEnvironment): string {
  return `${product}-${environment}`;
}

/** Creates detached editing state from a target, or a dev environment on the first server. */
export function targetFormOf(target: InventoryTarget | null, product: string, firstServer: string | null): TargetFormModel {
  return {
    slug: target?.slug ?? suggestedTargetSlug(product, DeploymentEnvironment.Dev),
    environment: target?.environment ?? DeploymentEnvironment.Dev,
    server: target?.server ?? firstServer ?? "",
    network: target?.network ?? "platform",
    root: target?.root ?? "/srv/wheelhouse",
    settings: target?.settings.map((setting) => ({ ...setting })) ?? [],
    smokeChecks: target?.smokeChecks.map((check) => ({ ...check })) ?? [],
    sites: target?.sites.map((site) => ({ ...site })) ?? [],
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

/**
 * The site rows with each known public site's baseline host filled in (decision 68): a site without a row, or with an
 * empty host or a baseline host derived before (another environment or server), takes the baseline host for this
 * environment and server; a host the owner named stays. Private sites and servers without a public address change
 * nothing; the deploy fills an empty host the same way.
 */
export function withBaselineHosts(
  sites: readonly SiteHost[],
  known: readonly KnownSite[],
  product: string,
  environment: DeploymentEnvironment,
  address: string | null,
): SiteHost[] {
  const rows = sites.map((site) => ({ ...site }));
  for (const site of known) {
    if (site.exposure !== "public") continue;
    const host = BaselineHost.of(product, site.name, environment, address, site.primary);
    if (!host) continue;
    const row = rows.find((item) => item.site.trim() === site.name);
    if (!row) rows.push({ site: site.name, host });
    else if (!row.host.trim() || BaselineHost.isBaseline(row.host)) row.host = host;
  }
  return rows;
}
