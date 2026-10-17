import {
  CatalogDeployer,
  CatalogEntryState,
  type CatalogEntry,
  type CatalogImportResult,
  type CatalogSource,
} from './Catalog';

/** Counts a word: `1 product`, `3 products`. @internal */
function count(value: number, noun: string): string {
  return `${value} ${noun}${value === 1 ? '' : 's'}`;
}

/** Pure rules for the catalog page: which entries import, the selection and the result line. */
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
   * The selection the operator can import: only importable slugs, each once, in catalog order. A slug the catalog no
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

  /** `owner/repository@abc1234`, the line naming where the catalog was read. */
  sourceLabel: (source: CatalogSource): string =>
    `${source.repository}@${source.commit ? source.commit.slice(0, 7) : source.ref}`,

  /** The line a finished import reports. */
  summary: (result: CatalogImportResult): string => {
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
