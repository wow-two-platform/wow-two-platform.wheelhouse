/**
 * One repository's release inside a release set: a product built from several repositories releases one of each
 * (decision 70), composed into one bundle that deploys, rolls back and promotes as a whole.
 */
export interface ReleaseComponent {
  /** The repository's name within the product, as the set's label names it (`studio`). */
  name: string | null;
  /** The repository, as `owner/name`. */
  repository: string | null;
  /** The repository's release tag, or `sha-…` for a commit build. */
  release: string | null;
  kind?: 'release' | 'candidate' | null;
  /** The commit the repository's release was built from. */
  sourceCommit?: string | null;
}
