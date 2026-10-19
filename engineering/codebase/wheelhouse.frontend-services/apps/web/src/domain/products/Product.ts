/** Whether a product or a service is in use; inactive ones stay hidden until the owner shows them. */
export const ProductStatus = {
  Active: 'active',
  Inactive: 'inactive',
} as const;
export type ProductStatus = (typeof ProductStatus)[keyof typeof ProductStatus];

/**
 * What deploys a product, as its catalog entry's `deploy` says: Wheelhouse (automatic deploys and promotions apply), its
 * own CI (the console never deploys it on its own and its pills say CI) or nothing.
 */
export const ProductDeploy = {
  Wheelhouse: 'wheelhouse',
  Ci: 'ci',
  None: 'none',
} as const;
export type ProductDeploy = (typeof ProductDeploy)[keyof typeof ProductDeploy];

/** How the product form names each deploy mode. */
export const ProductDeployLabels: Readonly<Record<ProductDeploy, string>> = {
  [ProductDeploy.Wheelhouse]: 'Wheelhouse',
  [ProductDeploy.Ci]: 'Its own CI',
  [ProductDeploy.None]: 'Nothing',
};

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

/** A product as the database defines it, with the workspace and status the owner records. */
export interface Product {
  /** The product's row identifier, when the host sends one; pins and catalog entries may name a product by it. */
  id?: string | undefined;
  /** The product's identifier everywhere: targets, releases and URLs. */
  slug: string;
  /** From the manifest when the repository carries a valid one, else the product's own row. */
  name: string;
  description: string;
  /** The product's public address, from its manifest; null when it gives none. */
  homepage: string | null;
  identity: ProductIdentity;
  /** The workspace the product belongs to; null while it belongs to none. */
  workspaceId: string | null;
  status: ProductStatus;
  /** What deploys the product: Wheelhouse, its own CI or nothing. */
  deploy: ProductDeploy;
  /** The services the owner marked inactive, by their key in the product's deployment. */
  inactiveServices: string[];
  repository: ProductRepository;
  /** Null when only hand-imported bundles deploy. */
  release: ProductRelease | null;
  iconUrl: string;
  environments: ProductEnvironment[];
}

/** A product's editable definition; the slug is fixed once the product exists. */
export interface SaveProductRequest {
  slug?: string;
  /** The workspace a new product joins; the host picks its default workspace when absent. Only creation sends it. */
  workspaceId?: string;
  name: string;
  description: string;
  repository: string;
  defaultBranch: string;
  release: ProductRelease | null;
  /** What deploys the product; the host starts a new one with Wheelhouse and keeps an existing one's when absent. */
  deploy?: ProductDeploy;
}
