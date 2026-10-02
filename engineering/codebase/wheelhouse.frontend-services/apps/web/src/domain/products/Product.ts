/** Where a catalog product stands in the portfolio. */
export const ProductLifecycle = {
  Idea: 'idea',
  Building: 'building',
  Live: 'live',
  Paused: 'paused',
  Killed: 'killed',
} as const;
export type ProductLifecycle = (typeof ProductLifecycle)[keyof typeof ProductLifecycle];

/** A site an environment publishes; a private one is reached over the private network only. */
export interface ProductSite {
  name: string;
  url: string;
  exposure: 'public' | 'private';
}

/** Where an environment's settings belong in a vault. */
export interface ProductSecrets {
  vault: string;
  namespace: string;
}

/** One environment of a catalog product. */
export interface ProductEnvironment {
  name: string;
  sites: ProductSite[];
  secrets: ProductSecrets | null;
}

/** A catalog product's source repository. */
export interface ProductRepository {
  /** `owner/name`. */
  name: string;
  url: string;
  defaultBranch: string;
}

/** One service's image repository. */
export interface ReleaseImage {
  service: string;
  image: string;
}

/** Where a product's published releases and commit builds come from. */
export interface ProductRelease {
  asset: string;
  workflow: string | null;
  images: ReleaseImage[];
}

/** A product as the database defines it, with the lifecycle the operator records. */
export interface Product {
  /** The product's identifier everywhere: targets, releases and URLs. */
  slug: string;
  name: string;
  description: string;
  lifecycle: ProductLifecycle;
  repository: ProductRepository;
  /** Null when only hand-imported bundles deploy. */
  release: ProductRelease | null;
  iconUrl: string;
  environments: ProductEnvironment[];
}

/** A product's editable definition; the slug is fixed once the product exists. */
export interface SaveProductRequest {
  slug?: string;
  name: string;
  description: string;
  repository: string;
  defaultBranch: string;
  release: ProductRelease | null;
}
