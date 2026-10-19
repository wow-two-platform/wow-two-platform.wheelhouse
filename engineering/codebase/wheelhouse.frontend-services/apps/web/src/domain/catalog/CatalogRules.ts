import {
  CatalogDeployer,
  CatalogEntryState,
  type Catalog,
  type CatalogEntry,
  type CatalogImportResult,
  type CatalogSource,
} from './Catalog';

/** One catalog workspace's apps, and how many of them can be imported, are in Wheelhouse, or deploy elsewhere. */
export interface CatalogWorkspaceGroup {
  slug: string;
  name: string;
  description: string;
  entries: CatalogEntry[];
  /** The slugs Wheelhouse deploys and has not imported, in catalog order. */
  importable: string[];
  inWheelhouse: number;
  /** Apps something other than Wheelhouse deploys, or nothing does. */
  notDeployable: number;
}

/** One hub's part of the catalog: where it was read, why it lists nothing if it does not, and its workspaces. */
export interface CatalogSourceGroup {
  source: CatalogSource;
  /** The hub's own name, else its repository. */
  name: string;
  /** The hub's workspaces with their apps, in its file's order. */
  groups: CatalogWorkspaceGroup[];
  /** How many apps the hub lists. */
  entries: number;
  /** How many of them can be imported. */
  importable: number;
}

/** How much of one workspace's importable apps the selection holds. */
export const WorkspaceSelection = { None: 'none', Some: 'some', All: 'all' } as const;
export type WorkspaceSelection = (typeof WorkspaceSelection)[keyof typeof WorkspaceSelection];

/** Counts a word: `1 product`, `3 products`. @internal */
function count(value: number, noun: string): string {
  return `${value} ${noun}${value === 1 ? '' : 's'}`;
}

/** Pure rules for the catalog page: the hubs, which entries import, the selection and the result line. */
export const CatalogRules = {
  /** Whether Wheelhouse deploys the entry and it is not a product yet. */
  isImportable: (entry: Pick<CatalogEntry, 'deploy' | 'productId'>): boolean =>
    entry.deploy === CatalogDeployer.Wheelhouse && entry.productId === null,

  /** Where the entry stands against Wheelhouse. */
  stateOf: (entry: Pick<CatalogEntry, 'deploy' | 'productId'>): CatalogEntryState =>
    entry.productId !== null
      ? CatalogEntryState.InWheelhouse
      : entry.deploy === CatalogDeployer.Wheelhouse
        ? CatalogEntryState.New
        : CatalogEntryState.External,

  /** The slugs of every importable entry, in catalog order. */
  importable: (entries: readonly CatalogEntry[]): string[] =>
    entries.filter(CatalogRules.isImportable).map((entry) => entry.slug),

  /**
   * The selection the owner can import: only importable slugs, each once, in catalog order. A slug the catalog no
   * longer lists, or one imported since, drops out.
   */
  selection: (entries: readonly CatalogEntry[], selected: readonly unknown[]): string[] => {
    const wanted = new Set(selected.filter((value): value is string => typeof value === 'string'));
    return CatalogRules.importable(entries).filter((slug) => wanted.has(slug));
  },

  /** Adds or removes one slug; a slug that cannot be imported leaves the selection as it was. */
  toggle: (entries: readonly CatalogEntry[], selected: readonly string[], slug: string): string[] => {
    const current = CatalogRules.selection(entries, selected);
    if (!CatalogRules.importable(entries).includes(slug)) return current;
    return current.includes(slug)
      ? current.filter((item) => item !== slug)
      : CatalogRules.selection(entries, [...current, slug]);
  },

  /**
   * The apps by workspace, in the catalog's workspace order; a workspace an app names but the catalog does not list
   * follows, under its slug. A workspace with no apps is left out.
   */
  groups: (catalog: Pick<Catalog, 'workspaces' | 'entries'>): CatalogWorkspaceGroup[] => {
    const listed = catalog.workspaces.map((workspace) => workspace.slug);
    const unlisted = catalog.entries.map((entry) => entry.workspace).filter((slug) => !listed.includes(slug));
    return [...new Set([...listed, ...unlisted])]
      .map((slug) => {
        const workspace = catalog.workspaces.find((item) => item.slug === slug);
        const entries = catalog.entries.filter((entry) => entry.workspace === slug);
        const states = entries.map(CatalogRules.stateOf);
        return {
          slug,
          name: workspace?.name ?? slug,
          description: workspace?.description ?? '',
          entries,
          importable: CatalogRules.importable(entries),
          inWheelhouse: states.filter((state) => state === CatalogEntryState.InWheelhouse).length,
          notDeployable: states.filter((state) => state === CatalogEntryState.External).length,
        };
      })
      .filter((group) => group.entries.length > 0);
  },

  /**
   * The catalog hub by hub, in the order Wheelhouse reads them, each with its workspaces grouped as {@link groups} does.
   * Workspaces and apps whose hub is not among the sources follow under a group of their own.
   */
  sources: (catalog: Pick<Catalog, 'sources' | 'workspaces' | 'entries'>): CatalogSourceGroup[] => {
    const read = catalog.sources.map((source) => source.repository);
    const stray = [...catalog.workspaces, ...catalog.entries].map((item) => item.source).filter((slug) => !read.includes(slug));
    const sources: CatalogSource[] = [
      ...catalog.sources,
      ...[...new Set(stray)].map((repository) => ({ repository, path: '', ref: '', name: null, commit: null, error: null })),
    ];
    return sources.map((source) => {
      const workspaces = catalog.workspaces.filter((workspace) => workspace.source === source.repository);
      const entries = catalog.entries.filter((entry) => entry.source === source.repository);
      return {
        source,
        name: source.name ?? source.repository,
        groups: CatalogRules.groups({ workspaces, entries }),
        entries: entries.length,
        importable: CatalogRules.importable(entries).length,
      };
    });
  },

  /** `5 apps · 2 importable` for a hub, or `Not listed` when its read failed. */
  sourceCountsLine: (group: Pick<CatalogSourceGroup, 'source' | 'entries' | 'importable'>): string =>
    group.source.error
      ? 'Not listed'
      : `${count(group.entries, 'app')} · ${group.importable ? `${group.importable} importable` : 'nothing to import'}`,

  /** `2 importable · 1 in Wheelhouse · 1 not deployable`, leaving out the counts that are zero. */
  countsLine: (group: Pick<CatalogWorkspaceGroup, 'importable' | 'inWheelhouse' | 'notDeployable'>): string =>
    [
      group.importable.length ? `${group.importable.length} importable` : 'Nothing to import',
      group.inWheelhouse ? `${group.inWheelhouse} in Wheelhouse` : null,
      group.notDeployable ? `${group.notDeployable} not deployable` : null,
    ]
      .filter((part): part is string => part !== null)
      .join(' · '),

  /** How much of the workspace's importable apps the selection holds; a workspace with none to import holds none. */
  workspaceSelection: (group: Pick<CatalogWorkspaceGroup, 'importable'>, selected: readonly string[]): WorkspaceSelection => {
    const held = group.importable.filter((slug) => selected.includes(slug)).length;
    return held === 0 ? WorkspaceSelection.None : held === group.importable.length ? WorkspaceSelection.All : WorkspaceSelection.Some;
  },

  /** Adds every importable app of the workspace to the selection, or takes them all out; other workspaces keep theirs. */
  selectWorkspace: (
    entries: readonly CatalogEntry[],
    selected: readonly string[],
    group: Pick<CatalogWorkspaceGroup, 'importable'>,
    isSelected: boolean,
  ): string[] =>
    CatalogRules.selection(
      entries,
      isSelected ? [...selected, ...group.importable] : selected.filter((slug) => !group.importable.includes(slug)),
    ),

  /** `owner/repository@abc1234`, the line naming where a hub's catalog was read. */
  sourceLabel: (source: CatalogSource): string =>
    `${source.repository}@${source.commit ? source.commit.slice(0, 7) : source.ref}`,

  /** The hub's catalog file on GitHub at the commit it was read from (else its ref); null for a source that is not a
   * GitHub repository or names no file. */
  sourceHref: (source: CatalogSource): string | null =>
    /^[\w.-]+\/[\w.-]+$/.test(source.repository) && source.path
      ? `https://github.com/${source.repository}/blob/${source.commit ?? source.ref}/${source.path}`
      : null,

  /** The notes an import left, two at most and a count of the rest; empty when it left none. */
  warningLine: (result: Pick<CatalogImportResult, 'warnings'>): string => {
    const shown = result.warnings.slice(0, 2).map((warning) => `${warning.slug}: ${warning.message}`);
    const more = result.warnings.length - shown.length;
    return shown.join('; ') + (more > 0 ? `; and ${more} more` : '');
  },

  /** The line a finished import reports. */
  summary: (result: Omit<CatalogImportResult, 'warnings'>): string => {
    const parts = [
      result.createdProducts ? `added ${count(result.createdProducts, 'product')}` : null,
      result.createdWorkspaces ? `created ${count(result.createdWorkspaces, 'workspace')}` : null,
      result.movedProducts ? `moved ${count(result.movedProducts, 'product')}` : null,
    ].filter((part): part is string => part !== null);
    if (!parts.length) return 'Nothing to import; the catalog and Wheelhouse already agree.';
    const text = parts.length > 1 ? `${parts.slice(0, -1).join(', ')} and ${parts.at(-1)}` : (parts[0] ?? '');
    return `${text.charAt(0).toUpperCase()}${text.slice(1)}.`;
  },
} as const;
