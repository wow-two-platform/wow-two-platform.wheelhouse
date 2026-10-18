/** One release between what a target runs and the release to promote, with its body as GitHub holds it. */
export interface ChangelogRelease {
  readonly release: string;
  readonly name: string;
  readonly publishedAt: string;
  /** Plain text, shown as written; never rendered as markup. */
  readonly body: string;
  readonly truncated: boolean;
  readonly url: string;
}

/** One commit of the compare between the two releases. */
export interface ChangelogCommit {
  readonly sha: string;
  readonly message: string;
  readonly author: string;
  readonly url: string;
}

/** What changed between what a target runs and the release to promote. */
export interface ReleaseChangelog {
  readonly from: string | null;
  readonly to: string;
  /** Newest first. */
  readonly releases: readonly ChangelogRelease[];
  /** Newest first; null when there is nothing to compare from or the compare could not be read. */
  readonly commits: readonly ChangelogCommit[] | null;
  readonly totalCommits: number | null;
  /** Changed files that look like schema migrations. */
  readonly migrations: readonly string[];
  readonly compareUrl: string | null;
  /** Why the commits are missing, when the compare could not be read. */
  readonly reason: string | null;
}
