import { ConsolePaths, ProductTab, type ConsoleLocation } from '@/application/navigation';
import type { DeploymentItem, DeploymentRange } from '@/domain/deployments';

import { DeploymentView, writeDeploymentQuery } from './DeploymentFilterQuery';

/** A release chosen in a deployments list's address: `release` (a version or a bundle) and, optionally, its `env`. */
export interface ReleaseSelection {
  readonly release: string;
  readonly environment: string | null;
}

/** The query keys a release selection owns. */
export const ReleaseQueryKeys = ['release', 'env'] as const;

/** The first value of a query key. @internal */
function first(value: unknown): string | null {
  const item = Array.isArray(value) ? value[0] : value;
  return typeof item === 'string' && item ? item : null;
}

/** The release a deployment carried, as its selection names it: the version, or the bundle when it names none. */
export function releaseOf(item: Pick<DeploymentItem, 'release' | 'bundleId'>): string | null {
  return item.release ?? item.bundleId ?? null;
}

/** Reads a release selection from a route query; none without `release`. */
export function readReleaseSelection(query: Readonly<Record<string, unknown>>): ReleaseSelection | null {
  const release = first(query['release']);
  return release ? { release, environment: first(query['env']) } : null;
}

/** Whether a deployment is one the selection chose: its release, in its environment when one is named. */
export function isSelectedRelease(
  item: Pick<DeploymentItem, 'release' | 'bundleId' | 'environment'>,
  selection: ReleaseSelection | null,
): boolean {
  return (
    selection !== null &&
    releaseOf(item) === selection.release &&
    (selection.environment === null || item.environment === selection.environment)
  );
}

/**
 * Where a deploy opens its release: the product page's Deployments tab as a list over `range` (so an older day stays
 * in reach), with `release` and `env` choosing its rows, which the list scrolls to. Null for a deploy whose product is
 * gone or that names no release.
 */
export function releaseLocation(
  item: Pick<DeploymentItem, 'product' | 'release' | 'bundleId' | 'environment'>,
  range: DeploymentRange,
): ConsoleLocation | null {
  const release = releaseOf(item);
  if (!item.product || !release) return null;
  const view = writeDeploymentQuery({
    filter: { entities: [], range },
    view: DeploymentView.List,
    day: null,
  });
  const query: Record<string, string> = {};
  for (const [key, value] of Object.entries(view)) if (typeof value === 'string') query[key] = value;
  query['release'] = release;
  if (item.environment) query['env'] = item.environment;
  return { path: ConsolePaths.product(item.product, ProductTab.Deployments), query };
}
