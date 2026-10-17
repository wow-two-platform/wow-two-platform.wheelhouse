import type { DeploymentItem } from './DeploymentItem';

/** One page of deployments, newest first. */
export interface DeploymentPage {
  items: DeploymentItem[];
  /** The next page's cursor; null on the last page. */
  nextCursor: string | null;
}
