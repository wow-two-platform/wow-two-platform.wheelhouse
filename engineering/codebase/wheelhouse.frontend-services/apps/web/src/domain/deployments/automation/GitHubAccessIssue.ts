/**
 * One thing keeping Wheelhouse from reaching GitHub: an owner without the GitHub App, a refused credential, a spent
 * rate limit, or an App set up by half. A later success for the owner clears it.
 */
export interface GitHubAccessIssue {
  /** The GitHub account it concerns; null for the GitHub App's own setup. */
  readonly owner: string | null;
  /** What went wrong and what follows, in plain words. */
  readonly cause: string;
  /** The HTTP status GitHub answered, when one did. */
  readonly status: number | null;
  /** When it was last seen. */
  readonly at: string;
}
