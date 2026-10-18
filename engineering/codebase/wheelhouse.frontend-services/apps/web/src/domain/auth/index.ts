/** The signed-in owner, from the GitHub session. */
export interface CurrentUser {
  login: string;
  name: string;
  avatar: string | null;
}

/** Service liveness. */
export interface SystemStatus {
  service: string;
  status: string;
  /** The API's informational version: `X.Y.Z+<commit>`. */
  version?: string;
  /** True when this instance drives the local rehearsal rig instead of real hosts. */
  localRig?: boolean;
}
