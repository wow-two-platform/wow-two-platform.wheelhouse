/** A product whose newest finished main build failed; a later success clears it. */
export interface BuildFailure {
  readonly product: string;
  readonly workspace: string | null;
  readonly runId: number;
  /** `failure`, `timed_out` or `startup_failure`. */
  readonly conclusion: string;
  readonly commit: string | null;
  readonly title: string;
  readonly at: string | null;
  /** The run on GitHub. */
  readonly url: string;
}
