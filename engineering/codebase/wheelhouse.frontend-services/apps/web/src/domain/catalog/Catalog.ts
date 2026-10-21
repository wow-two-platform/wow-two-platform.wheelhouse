/** One hub the catalog was read from: `catalog.yml` in the hub repository, at one commit, or why it is not listed. */
export interface CatalogSource {
  /** The hub repository, `owner/name`; workspaces and entries name it as their `source`. */
  repository: string;
  path: string;
  ref: string;
  /** The hub's own name for itself; null when its file gives none or was not read. */
  name: string | null;
  /** The full SHA the file was read at; null when the host could not resolve it. */
  commit: string | null;
  /** Why the hub lists nothing, naming the hub; null when its workspaces and entries are listed. */
  error: string | null;
}

/** A workspace the catalog groups its apps under. */
export interface CatalogWorkspace {
  slug: string;
  name: string;
  description: string;
  /** The repository of the hub that declares it. */
  source: string;
}

/** One app as a hub's catalog lists it. */
export interface CatalogEntry {
  slug: string;
  name: string;
  /** The repository of the hub that lists it. */
  source: string;
  /** The slug of the catalog workspace the app belongs to. */
  workspace: string;
  /** What the app is, such as `app`, `platform` or `library`. */
  kind: string;
  /** Who deploys the app; only `wheelhouse` apps can be imported. */
  deploy: string;
  purpose: string;
  /** GitHub `owner/name`, primary first; several make one product built from several repositories (decision 70). */
  repositories: string[];
  /** The products the entry supersedes: importing it retires each one nothing ever deployed to. */
  replaces: string[];
  /** The Wheelhouse product the entry already is; null while it is not in Wheelhouse. */
  productId: string | null;
}

/** The merged catalog: every hub read, in Wheelhouse's order, then the listed hubs' workspaces and apps. */
export interface Catalog {
  sources: CatalogSource[];
  workspaces: CatalogWorkspace[];
  entries: CatalogEntry[];
}

/** A note about one entry of an import: skipped, or created without something. */
export interface CatalogImportWarning {
  slug: string;
  /** What happened, in display-safe words. */
  message: string;
}

/** What an import changed. */
export interface CatalogImportResult {
  createdWorkspaces: number;
  createdProducts: number;
  /** Existing products that moved into the workspace the catalog names. */
  movedProducts: number;
  /** Existing products whose deploy mode the import set from their entry's `deploy`. */
  updatedProducts: number;
  /** Products the import retired because an imported entry replaces them and nothing had deployed to them. */
  retiredProducts: number;
  warnings: CatalogImportWarning[];
}

/** The deployer an importable entry names. */
export const CatalogDeployer = { Wheelhouse: 'wheelhouse' } as const;

/** Where an entry stands against Wheelhouse. */
export const CatalogEntryState = {
  /** Wheelhouse deploys it and it is not a product yet: importable. */
  New: 'new',
  /** It is a Wheelhouse product already. */
  InWheelhouse: 'in-wheelhouse',
  /** Something other than Wheelhouse deploys it. */
  External: 'external',
} as const;
export type CatalogEntryState = (typeof CatalogEntryState)[keyof typeof CatalogEntryState];
