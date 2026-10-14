import { ProductIdentitySource, type Product } from './Product';

/** Where a product's manifest sits in its repository — the wow-two product manifest convention. */
export const ProductManifestPath = 'product/product.json';

/**
 * Whether the console must show a product's name, description and homepage read-only: they come from the manifest in
 * the repository, and edits go there. A product being created, or one whose repository carries no valid manifest, is
 * edited in the console.
 */
export function isIdentityReadOnly(product: Product | null | undefined): boolean {
  return product?.identity.source === ProductIdentitySource.Manifest;
}

/** The one line the product form shows beside read-only identity fields; null when they are editable. */
export function identityNoteOf(product: Product | null | undefined): string | null {
  return isIdentityReadOnly(product) ? `Edited in ${ProductManifestPath} in the repository.` : null;
}

/**
 * The line that says the repository's newest manifest is ignored, or null when nothing is. While a valid copy
 * applies it keeps applying, and the line says so.
 */
export function identityProblemOf(product: Product | null | undefined): string | null {
  const problem = product?.identity.problem;
  if (!problem) return null;
  const kept = isIdentityReadOnly(product) ? ' The last valid copy stays.' : '';
  return `${ProductManifestPath} is ignored: ${problem}${kept}`;
}

/** The first seven characters of a commit, as developers write it; null for no commit. */
export function shortCommitOf(commit: string | null | undefined): string | null {
  return commit ? commit.slice(0, 7) : null;
}

/**
 * The address a homepage link may point at: only an http or https address, so a value the host should never have
 * accepted still cannot become a script link. Null for no homepage or any other scheme.
 */
export function homepageLinkOf(product: Product | null | undefined): { href: string; host: string } | null {
  const homepage = product?.homepage;
  if (!homepage) return null;
  try {
    const url = new URL(homepage);
    return url.protocol === 'https:' || url.protocol === 'http:' ? { href: url.href, host: url.host } : null;
  } catch {
    return null;
  }
}

/** One service of a product as the console lists it. */
export interface ServiceIdentityView {
  /** The service's key in `deploy.yml`. */
  service: string;
  /** The manifest's name for the service, or its key when the manifest gives none. */
  label: string;
  description: string | null;
}

/** The services the manifest names, in key order, each with the name to show. */
export function serviceIdentitiesOf(product: Product | null | undefined): ServiceIdentityView[] {
  return [...(product?.identity.services ?? [])]
    .sort((a, b) => (a.service < b.service ? -1 : a.service > b.service ? 1 : 0))
    .map((service) => ({
      service: service.service,
      label: service.name ?? service.service,
      description: service.description,
    }));
}
