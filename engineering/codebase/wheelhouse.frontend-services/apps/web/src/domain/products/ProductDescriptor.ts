/** One service a product's `deploy.yml` declares; every environment of the product runs it. */
export interface ProductDescriptorService {
  /** The service's key under `services:`. */
  service: string;
  /** The platform services it consumes, such as `postgres`. */
  needs: string[];
}

/** One site a product's `deploy.yml` publishes. */
export interface ProductDescriptorSite {
  name: string;
  /** A private site answers on the private network only, through a tailnet node per environment. */
  exposure: 'public' | 'private';
  /** The front door, whose baseline host carries no site name. */
  primary: boolean;
}

/** What a product's `deploy.yml` declares at its branch head; `problem` says why it could not be read. */
export interface ProductDescriptor {
  /** The commit it was read from; null when it could not be read. */
  commit: string | null;
  problem: string | null;
  services: ProductDescriptorService[];
  sites: ProductDescriptorSite[];
}

/** Pure rules over a product's descriptor. */
export const ProductDescriptorRules = {
  /** The services every environment runs, in file order. */
  serviceNames: (descriptor: Pick<ProductDescriptor, 'services'>): string[] =>
    descriptor.services.map((service) => service.service),

  /** The services that need PostgreSQL, so the product takes a dev and a prod database. */
  postgresServices: (descriptor: Pick<ProductDescriptor, 'services'>): string[] =>
    descriptor.services.filter((service) => service.needs.includes('postgres')).map((service) => service.service),

  /** The sites reached over the private network only, each needing a tailnet node per environment. */
  privateSites: (descriptor: Pick<ProductDescriptor, 'sites'>): string[] =>
    descriptor.sites.filter((site) => site.exposure === 'private').map((site) => site.name),
} as const;
