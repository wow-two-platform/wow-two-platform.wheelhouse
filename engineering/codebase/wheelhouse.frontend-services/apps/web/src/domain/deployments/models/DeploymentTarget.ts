/** A code-owned product environment on one server. */
export interface DeploymentTarget {
  id: string;
  product: string;
  environment: string;
  serverId: string;
  provider: string;
  host: string;
  /** Dev takes a build of any commit or branch; prod takes published releases only. */
  acceptsCandidates: boolean;
  /** Prod on the local server deploys only after the owner types the target ID. */
  needsConfirmation: boolean;
  /** Prod takes a release only after it succeeded on dev; typing the target ID skips the pass. */
  requiresDevPass: boolean;
  /** Runs Wheelhouse itself: never in a fleet; its prod is promoted by the owner alone (decision 64). */
  self: boolean;
}
