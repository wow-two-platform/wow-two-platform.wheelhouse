import type { Product } from '@/domain/products';
import { ProductCondition, type ProductFacts } from './ProductFacts';

/** How the Products page orders products inside each workspace. */
export const ProductSort = {
  /** By name; the default. */
  Name: 'name',
  /** The most recently deployed first. */
  LastDeployed: 'last-deployed',
  /** The longest without a deploy first. */
  OldestUpdated: 'oldest-updated',
  /** Failing, then needing a look, first. */
  FailedFirst: 'failed-first',
  /** Healthy first, the most recently deployed leading. */
  SucceededFirst: 'succeeded-first',
} as const;
export type ProductSort = (typeof ProductSort)[keyof typeof ProductSort];

/** Each sort's label. */
export const ProductSortLabels: Readonly<Record<ProductSort, string>> = {
  name: 'Name',
  'last-deployed': 'Last deployed',
  'oldest-updated': 'Oldest updated',
  'failed-first': 'Failed first',
  'succeeded-first': 'Succeeded first',
};

/** Which products the status filter keeps. */
export const StatusFilter = {
  All: 'all',
  Failing: 'failing',
  /** Rolled back, degraded, or unread. */
  Attention: 'attention',
  /** Healthy, or deploying with nothing wrong. */
  Healthy: 'healthy',
  /** Nothing deployed anywhere. */
  Idle: 'idle',
} as const;
export type StatusFilter = (typeof StatusFilter)[keyof typeof StatusFilter];

/** Each status filter's label. */
export const StatusFilterLabels: Readonly<Record<StatusFilter, string>> = {
  all: 'Any status',
  failing: 'Failing',
  attention: 'Needs a look',
  healthy: 'Healthy',
  idle: 'Not deployed',
};

/** The Products page's search, filter and sort, as its address carries them. */
export interface ProductFilterState {
  readonly search: string;
  /** Workspace slugs; empty for every workspace. */
  readonly workspaces: readonly string[];
  readonly status: StatusFilter;
  readonly sort: ProductSort;
}

/** The filter in force when the address names none. */
export const DefaultProductFilter: ProductFilterState = {
  search: '',
  workspaces: [],
  status: StatusFilter.All,
  sort: ProductSort.Name,
};

/** Reads one query value the way vue-router hands it over. @internal */
function first(value: unknown): string | null {
  const item = Array.isArray(value) ? value[0] : value;
  return typeof item === 'string' ? item : null;
}

/** Whether a value is one of a const object's values. @internal */
function isOneOf<T extends string>(values: Readonly<Record<string, T>>, value: string | null): value is T {
  return value !== null && (Object.values(values) as string[]).includes(value);
}

/**
 * The filter's address form: `q` (search), `workspace` (comma-separated slugs), `status` and `sort`, each left out at
 * its default, so a plain `/products` is the default view. Unknown values read as the default.
 */
export const ProductFilterQuery = {
  parse: (query: Readonly<Record<string, unknown>>): ProductFilterState => {
    const status = first(query['status']);
    const sort = first(query['sort']);
    return {
      search: (first(query['q']) ?? '').slice(0, 100),
      workspaces: [...new Set((first(query['workspace']) ?? '').split(',').map((slug) => slug.trim()).filter(Boolean))],
      status: isOneOf(StatusFilter, status) ? status : StatusFilter.All,
      sort: isOneOf(ProductSort, sort) ? sort : ProductSort.Name,
    };
  },
  toQuery: (state: ProductFilterState): Record<string, string> => {
    const query: Record<string, string> = {};
    if (state.search.trim()) query['q'] = state.search.trim();
    if (state.workspaces.length) query['workspace'] = state.workspaces.join(',');
    if (state.status !== StatusFilter.All) query['status'] = state.status;
    if (state.sort !== ProductSort.Name) query['sort'] = state.sort;
    return query;
  },
  /** Whether anything narrows the products; sorting alone does not. */
  isNarrowed: (state: ProductFilterState): boolean =>
    Boolean(state.search.trim()) || state.workspaces.length > 0 || state.status !== StatusFilter.All,
} as const;

/** The conditions each status filter keeps. @internal */
const StatusConditions: Readonly<Record<Exclude<StatusFilter, 'all'>, readonly ProductCondition[]>> = {
  failing: [ProductCondition.Failing],
  attention: [ProductCondition.Attention, ProductCondition.Unknown],
  healthy: [ProductCondition.Healthy, ProductCondition.Deploying],
  idle: [ProductCondition.Idle],
};

/** Each condition's place when failures lead. @internal */
const FailedFirstRank: Readonly<Record<ProductCondition, number>> = {
  failing: 0, attention: 1, unknown: 2, deploying: 3, healthy: 4, idle: 5,
};
/** Each condition's place when successes lead. @internal */
const SucceededFirstRank: Readonly<Record<ProductCondition, number>> = {
  healthy: 0, deploying: 1, unknown: 2, attention: 3, failing: 4, idle: 5,
};

/** Pure rules applying the filter and the sort to products. */
export const ProductFilterRules = {
  /** Whether every word of the search appears in the product's name, slug or description. */
  matchesSearch: (product: Product, search: string): boolean => {
    const words = search.toLowerCase().split(/\s+/).filter(Boolean);
    if (!words.length) return true;
    const text = `${product.name} ${product.slug} ${product.description}`.toLowerCase();
    return words.every((word) => text.includes(word));
  },

  /** Whether the product's condition passes the status filter; unknown facts pass only "any status". */
  matchesStatus: (facts: ProductFacts | undefined, status: StatusFilter): boolean =>
    status === StatusFilter.All || (facts !== undefined && StatusConditions[status].includes(facts.condition)),

  /** The products the search and status keep, in the sort's order. The workspace filter applies to whole groups. */
  apply: (
    products: readonly Product[],
    factsOf: (slug: string) => ProductFacts | undefined,
    state: ProductFilterState,
  ): Product[] =>
    products
      .filter((product) => ProductFilterRules.matchesSearch(product, state.search)
        && ProductFilterRules.matchesStatus(factsOf(product.slug), state.status))
      .sort(ProductFilterRules.comparer(state.sort, factsOf)),

  /** The order a sort puts two products in; ties fall back to the name, then the slug. */
  comparer: (sort: ProductSort, factsOf: (slug: string) => ProductFacts | undefined) => {
    const byName = (left: Product, right: Product) =>
      left.name.localeCompare(right.name) || left.slug.localeCompare(right.slug);
    const deployed = (product: Product) => {
      const at = factsOf(product.slug)?.lastDeployedAt;
      return at ? Date.parse(at) : null;
    };
    /** Newest or oldest first; never-deployed products close the list either way. */
    const byDate = (direction: 1 | -1) => (left: Product, right: Product) => {
      const a = deployed(left);
      const b = deployed(right);
      if (a === null || b === null) return (a === null ? 1 : 0) - (b === null ? 1 : 0) || byName(left, right);
      return (a - b) * direction || byName(left, right);
    };
    const byRank = (ranks: Readonly<Record<ProductCondition, number>>, then: (l: Product, r: Product) => number) =>
      (left: Product, right: Product) => {
        const a = ranks[factsOf(left.slug)?.condition ?? ProductCondition.Idle];
        const b = ranks[factsOf(right.slug)?.condition ?? ProductCondition.Idle];
        return a - b || then(left, right);
      };
    switch (sort) {
      case ProductSort.LastDeployed:
        return byDate(-1);
      case ProductSort.OldestUpdated:
        return byDate(1);
      case ProductSort.FailedFirst:
        return byRank(FailedFirstRank, byName);
      case ProductSort.SucceededFirst:
        return byRank(SucceededFirstRank, byDate(-1));
      default:
        return byName;
    }
  },
} as const;
