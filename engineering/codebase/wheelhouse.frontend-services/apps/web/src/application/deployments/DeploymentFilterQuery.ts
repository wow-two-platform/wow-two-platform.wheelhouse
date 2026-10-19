import {
  DefaultPreset,
  DefaultRange,
  DeploymentFilters,
  EntityKeys,
  EntitySelection,
  RangePresets,
  StatusFilter,
  readStatusFilter,
  type DeploymentFilterState,
  type DeploymentRange,
} from '@/domain/deployments';

/** How the deployments panel shows its filter's deployments. */
export const DeploymentView = { Grid: 'grid', List: 'list' } as const;
export type DeploymentView = (typeof DeploymentView)[keyof typeof DeploymentView];

/** Everything a deployments view keeps in its address, so a link reproduces it. */
export interface DeploymentViewState {
  readonly filter: DeploymentFilterState;
  readonly view: DeploymentView;
  /** The calendar day expanded under the grid, `YYYY-MM-DD`; null when none is. */
  readonly day: string | null;
}

/** The query keys a deployments view owns; anything else in the address is left alone. */
export const DeploymentQueryKeys = ['workspace', 'product', 'service', 'status', 'range', 'from', 'to', 'view', 'day'] as const;

const DATE = /^\d{4}-\d{2}-\d{2}$/;

/** Every value of a query key, whether vue-router hands over a string, an array or nothing. @internal */
function all(value: unknown): string[] {
  const items = Array.isArray(value) ? value : value === undefined || value === null ? [] : [value];
  return items.filter((item): item is string => typeof item === 'string' && item.length > 0);
}

/** The first value of a query key. @internal */
function first(value: unknown): string | null {
  return all(value)[0] ?? null;
}

/**
 * Reads a deployments view from a route query: repeated `workspace`, `product` and `service` (`product/service`),
 * `status` (`needs-action`, `look` or `succeeded`; the old `failed` reads as needs-action), a `range` preset or custom `from` and `to` (local `YYYY-MM-DDTHH:mm`), `view`
 * (`list`) and the expanded `day`. A malformed value falls back to its default rather than failing the page.
 */
export function readDeploymentQuery(query: Readonly<Record<string, unknown>>): DeploymentViewState {
  const entities = EntitySelection.normalize([
    ...all(query['workspace']).map(EntityKeys.workspace),
    ...all(query['product']).map(EntityKeys.product),
    ...all(query['service']).map((value) => `s:${value}`),
  ]);
  const status = readStatusFilter(first(query['status']));
  const from = first(query['from']);
  const to = first(query['to']);
  const preset = RangePresets.find((item) => item.value === first(query['range']))?.value;
  const range: DeploymentRange =
    from && to && DeploymentFilters.isValidCustom(from, to)
      ? { kind: 'custom', from, to }
      : preset
        ? { kind: 'preset', preset }
        : DefaultRange;
  const day = first(query['day']);
  return {
    filter: { entities, status, range },
    view: first(query['view']) === 'list' ? DeploymentView.List : DeploymentView.Grid,
    day: day && DATE.test(day) ? day : null,
  };
}

/** Writes a deployments view as route query values, leaving out every default so a plain link stays plain. */
export function writeDeploymentQuery(state: DeploymentViewState): Record<string, string | string[]> {
  const query: Record<string, string | string[]> = {};
  const workspaces: string[] = [];
  const products: string[] = [];
  const services: string[] = [];
  for (const key of state.filter.entities) {
    const entity = EntityKeys.parse(key);
    if (entity?.kind === 'workspace') workspaces.push(entity.workspace);
    else if (entity?.kind === 'product') products.push(entity.product);
    else if (entity?.kind === 'service') services.push(`${entity.product}/${entity.service}`);
  }
  if (workspaces.length) query['workspace'] = workspaces.length === 1 ? workspaces[0]! : workspaces;
  if (products.length) query['product'] = products.length === 1 ? products[0]! : products;
  if (services.length) query['service'] = services.length === 1 ? services[0]! : services;
  if (state.filter.status !== StatusFilter.All) query['status'] = state.filter.status;
  const range = state.filter.range;
  if (range.kind === 'custom') {
    query['from'] = range.from;
    query['to'] = range.to;
  } else if (range.preset !== DefaultPreset) query['range'] = range.preset;
  if (state.view === DeploymentView.List) query['view'] = DeploymentView.List;
  if (state.day) query['day'] = state.day;
  return query;
}

/** Replaces a view's own keys in a route query and keeps every other key. */
export function mergeDeploymentQuery(
  current: Readonly<Record<string, unknown>>,
  state: DeploymentViewState,
): Record<string, string | string[]> {
  const kept = Object.fromEntries(
    Object.entries(current).filter(
      (entry): entry is [string, string | string[]] =>
        !(DeploymentQueryKeys as readonly string[]).includes(entry[0]) &&
        (typeof entry[1] === 'string' || (Array.isArray(entry[1]) && entry[1].every((item) => typeof item === 'string'))),
    ),
  );
  return { ...kept, ...writeDeploymentQuery(state) };
}
