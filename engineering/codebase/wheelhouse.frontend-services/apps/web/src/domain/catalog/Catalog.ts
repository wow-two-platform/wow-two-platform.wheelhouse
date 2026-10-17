/** Where the wow2 catalog was read from: `catalog.yml` in the workspace repository, at one commit. */
export interface CatalogSource {
  repository: string;
  path: string;
  ref: string;
  /** The full SHA the file was read at; null when the host could not resolve it. */
  commit: string | null;
}

/** A workspace the catalog groups its apps under. */
export interface CatalogWorkspace {
  slug: string;
  name: string;
  description: string;
}

/** One wow2 app as the catalog lists it. */
export interface CatalogEntry {
  slug: string;
  name: string;
  /** The slug of the catalog workspace the app belongs to. */
  workspace: string;
  /** What the app is, such as `app`, `platform` or `library`. */
  kind: string;
  /** Who deploys the app; only `wheelhouse` apps can be imported. */
  deploy: string;
  purpose: string;
  repositories: string[];
  /** The Wheelhouse product the entry already is; null while it is not in Wheelhouse. */
  productId: string | null;
}

/** The whole catalog: its source, workspaces and apps. */
export interface Catalog {
  source: CatalogSource;
  workspaces: CatalogWorkspace[];
  entries: CatalogEntry[];
}

/** A note about one entry of an import: skipped, or created without something. */
export interface CatalogImportWarning {
  slug: string;
  /** What happened, in operator-safe words. */
  message: string;
}

/** What an import changed. */
export interface CatalogImportResult {
  createdWorkspaces: number;
  createdProducts: number;
  /** Existing products that moved into the workspace the catalog names. */
  movedProducts: number;
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
