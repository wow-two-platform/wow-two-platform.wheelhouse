import { ConsolePaths } from './ConsolePaths';

/** The primary destinations' addresses, in the order every screen class shows them: Home, then the entities. */
export const PrimaryPaths = [ConsolePaths.home, ConsolePaths.products, ConsolePaths.deployments, ConsolePaths.servers] as const;
export type PrimaryPath = (typeof PrimaryPaths)[number];

/** Whether a path is a place or a page below it: `/deployments` answers `/deployments/…` too. @internal */
function within(path: string, place: string): boolean {
  return path === place || path.startsWith(`${place}/`);
}

/**
 * The primary destination a path belongs to, or null for a page the profile menu opens. Products covers the browser,
 * a workspace (`/w/…`) and a product page (`/p/…`): every product and its services live under it.
 */
export function primaryDestinationOf(path: string): PrimaryPath | null {
  if (path === ConsolePaths.home) return ConsolePaths.home;
  if (within(path, ConsolePaths.products) || within(path, '/w') || within(path, '/p')) return ConsolePaths.products;
  if (within(path, ConsolePaths.deployments)) return ConsolePaths.deployments;
  if (within(path, ConsolePaths.servers)) return ConsolePaths.servers;
  return null;
}
