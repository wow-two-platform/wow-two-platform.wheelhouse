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

/** Where a product's name, description and homepage come from. */
export const ProductIdentitySource = {
  /** The cached copy of `product/product.json` in the product's repository; edited there. */
  Manifest: 'manifest',
  /** The product's own row, edited in the console. */
  Record: 'record',
} as const;
export type ProductIdentitySource = (typeof ProductIdentitySource)[keyof typeof ProductIdentitySource];

/** The name and description a product's manifest gives one of its services. */
export interface ProductServiceIdentity {
  /** The service's key in the product's `deploy.yml`. */
  service: string;
  name: string | null;
  description: string | null;
}

/** Where a product's identity text comes from and what its repository says about its services. */
export interface ProductIdentity {
  source: ProductIdentitySource;
  /** The full SHA of the commit the manifest was read from; null when the product's own row is the source. */
  commit: string | null;
  /** Why the repository's newest manifest is ignored; the last valid copy, if any, still applies. */
  problem: string | null;
  services: ProductServiceIdentity[];
}

/** A product as the database defines it, with the lifecycle the operator records. */
export interface Product {
  /** The product's identifier everywhere: targets, releases and URLs. */
  slug: string;
  /** From the manifest when the repository carries a valid one, else the product's own row. */
  name: string;
  description: string;
  /** The product's public address, from its manifest; null when it gives none. */
  homepage: string | null;
  identity: ProductIdentity;
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
