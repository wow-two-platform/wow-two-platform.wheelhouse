import { homepageLinkOf } from '@/domain/products/ProductIdentity';
import type { Product } from '@/domain/products';
import { environmentRank } from '@/domain/targets';

/** One address a site answers on in one environment. */
export interface SiteAddress {
  readonly href: string;
  readonly host: string;
}

/** One front-facing site of a product, with its address in each environment that publishes it. */
export interface SiteLink {
  /** The site's name in the product's manifest, such as `web` or `admin`. */
  readonly name: string;
  /** Reached over the private network only, in any environment. */
  readonly private: boolean;
  /** Each environment's address, by environment. */
  readonly addresses: Readonly<Record<string, SiteAddress>>;
}

/** Only an http or https address may become a link. @internal */
function addressOf(url: string): SiteAddress | null {
  try {
    const parsed = new URL(url);
    return parsed.protocol === 'https:' || parsed.protocol === 'http:' ? { href: parsed.href, host: parsed.host } : null;
  } catch {
    return null;
  }
}

/** Each site the product's environments publish, public before private, then by name. */
export function siteLinksOf(product: Product | null | undefined): SiteLink[] {
  const sites = new Map<string, { private: boolean; addresses: Record<string, SiteAddress> }>();
  const environments = [...(product?.environments ?? [])].sort(
    (left, right) => environmentRank(left.name) - environmentRank(right.name),
  );
  for (const environment of environments)
    for (const site of environment.sites) {
      const address = addressOf(site.url);
      if (!address) continue;
      const entry = sites.get(site.name) ?? { private: false, addresses: {} };
      entry.addresses[environment.name] = address;
      entry.private ||= site.exposure === 'private';
      sites.set(site.name, entry);
    }
  return [...sites.entries()]
    .map(([name, entry]) => ({ name, private: entry.private, addresses: entry.addresses }))
    .sort((left, right) => Number(left.private) - Number(right.private) || left.name.localeCompare(right.name));
}

/**
 * The one address a product's open-site control can open directly: its only site in its only environment, or its
 * homepage when it publishes no site. Null when there are several, so a menu lists them, or none at all.
 */
export function directSiteOf(product: Product | null | undefined, links: readonly SiteLink[]): SiteAddress | null {
  const addresses = links.flatMap((link) => Object.values(link.addresses));
  if (addresses.length === 1) return addresses[0] ?? null;
  if (addresses.length === 0) return homepageLinkOf(product);
  return null;
}
