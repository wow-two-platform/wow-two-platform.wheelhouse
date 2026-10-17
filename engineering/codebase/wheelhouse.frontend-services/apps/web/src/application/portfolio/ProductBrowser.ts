import { ConsolePaths } from '@/application/navigation/ConsolePaths';
import type { PortfolioGroup } from './Portfolio';

/** One filter chip of the products browser: All, or one workspace. */
export interface WorkspaceChip {
  /** The workspace's slug; null for All. */
  readonly slug: string | null;
  readonly label: string;
  /** The products the chip shows, following "Show inactive". */
  readonly count: number;
  readonly path: string;
}

/** What the products browser shows for the chosen workspace. */
export interface BrowserView {
  /** The workspace sections to draw: every group for All, the chosen one alone otherwise. */
  readonly groups: readonly PortfolioGroup[];
  /** True when the address names a workspace the console does not know. */
  readonly missing: boolean;
}

/**
 * The browser's chips: All, then each workspace in custom order. Products without a known workspace show under All
 * only, so they have no chip of their own.
 */
export function workspaceChips(groups: readonly PortfolioGroup[]): WorkspaceChip[] {
  const all: WorkspaceChip = {
    slug: null,
    label: 'All',
    count: groups.reduce((sum, group) => sum + group.products.length, 0),
    path: ConsolePaths.products,
  };
  return [
    all,
    ...groups.flatMap((group) =>
      group.workspace
        ? [{
            slug: group.workspace.slug,
            label: group.workspace.name,
            count: group.products.length,
            path: ConsolePaths.workspace(group.workspace.slug),
          }]
        : [],
    ),
  ];
}

/** The sections for the chosen workspace: all of them without one, the workspace's own, or none when it is unknown. */
export function browse(groups: readonly PortfolioGroup[], workspace: string | null): BrowserView {
  if (!workspace) return { groups, missing: false };
  const chosen = groups.find((group) => group.workspace?.slug === workspace);
  return chosen ? { groups: [chosen], missing: false } : { groups: [], missing: true };
}
