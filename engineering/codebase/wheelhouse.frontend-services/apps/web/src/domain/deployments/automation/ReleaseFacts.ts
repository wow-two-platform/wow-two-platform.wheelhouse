/** What a release's bundle declares that a deploy should weigh. */
export interface ReleaseFacts {
  readonly id: string;
  readonly product: string;
  readonly release: string;
  readonly kind: string;
  readonly sourceCommit: string;
  /** False when a failed rollout cannot restore the previous images on its own. */
  readonly rollbackCompatible: boolean;
  /** True when the release declares schema migrations; null while its bundle does not say. */
  readonly migrations: boolean | null;
}

/** The risks a prod deploy of a release carries, in words; empty when none is known. */
export function rollbackRisks(facts: { readonly rollbackCompatible: boolean | null; readonly migrations?: boolean | null; readonly hasMigrations?: boolean | null } | null | undefined): string[] {
  if (!facts) return [];
  return [
    ...(facts.rollbackCompatible === false ? ["Can't roll back automatically"] : []),
    ...((facts.migrations ?? facts.hasMigrations) === true ? ["Has migrations"] : []),
  ];
}
