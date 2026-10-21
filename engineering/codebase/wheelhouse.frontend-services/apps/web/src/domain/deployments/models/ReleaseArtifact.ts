import type { ReleaseComponent } from './ReleaseComponent';

/** Distinguishes a published release (a version tag) from a build of a commit, which only dev takes. */
export type ReleaseKind = 'release' | 'candidate';

/** A release bundle or a commit's build available for deployment. */
export interface ReleaseArtifact {
  id: string;
  product: string;
  release: string;
  kind: ReleaseKind;
  prerelease: boolean;
  publishedAt: string;
  provider: string;
  /** The built commit and its branch, for a commit's build. */
  commit?: string | null;
  branch?: string | null;
  /** When a commit's build expires from the catalog. */
  expiresAt?: string | null;
  /** Each repository's release, for a release set of a product built from several repositories. */
  components?: ReleaseComponent[] | null;
}
