import { ProductIdentitySource, type Product } from './Product';

/** Where a product declares its identity in its repository — the deploy descriptor convention's § Identity. */
export const ProductDescriptorPath = 'engineering/deployment/deploy.yml';

/** The retired identity file the host still reads, with a notice, while the descriptor declares no identity. */
export const RetiredManifestPath = 'product/product.json';

/**
 * Whether the console must show a product's name, description and homepage read-only: they come from the repository,
 * and edits go there. A product being created, or one whose repository declares no valid identity, is edited in the
 * console.
 */
export function isIdentityReadOnly(product: Product | null | undefined): boolean {
  return product?.identity.source === ProductIdentitySource.Manifest;
}

/** The one line the product form shows beside read-only identity fields; null when they are editable. */
export function identityNoteOf(product: Product | null | undefined): string | null {
  return isIdentityReadOnly(product) ? `Edited in ${product?.identity.file ?? ProductDescriptorPath} in the repository.` : null;
}

/**
 * The one warning a product's identity carries, or null when it carries none: first why the repository's newest
 * identity is ignored (the host names the file; while a valid copy applies it keeps applying, and the line says so),
 * else what the owner should change, such as moving a retired product/product.json into deploy.yml.
 */
export function identityNoticeOf(product: Product | null | undefined): string | null {
  const problem = product?.identity.problem;
  if (problem) return `${problem}${isIdentityReadOnly(product) ? ' The last valid copy stays.' : ''}`;
  return product?.identity.notice ?? null;
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
  /** The repository's name for the service, or its key when it gives none. */
  label: string;
  description: string | null;
}

/** The services the repository names, in key order, each with the name to show. */
export function serviceIdentitiesOf(product: Product | null | undefined): ServiceIdentityView[] {
  return [...(product?.identity.services ?? [])]
    .sort((a, b) => (a.service < b.service ? -1 : a.service > b.service ? 1 : 0))
    .map((service) => ({
      service: service.service,
      label: service.name ?? service.service,
      description: service.description,
    }));
}
