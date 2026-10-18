/** What a product's builds cost over the last 30 days. */
export interface BuildUsage {
  readonly product: string;
  readonly since: string;
  readonly days: number;
  readonly actions: {
    readonly runs: number;
    /** Wall-clock minutes of finished runs; a public repository's minutes are free. */
    readonly minutes: number;
    readonly workflows: ReadonlyArray<{ readonly name: string; readonly minutes: number }>;
    /** False when the product ran more workflows than one read lists (300). */
    readonly complete: boolean;
  };
  /** Each service image's GHCR package and how many versions it holds; null where the token may not read packages. */
  readonly packages: ReadonlyArray<{ readonly service: string; readonly package: string; readonly versions: number | null }>;
  /** Always null: GitHub's API reports no container package size. */
  readonly storageBytes: number | null;
}
