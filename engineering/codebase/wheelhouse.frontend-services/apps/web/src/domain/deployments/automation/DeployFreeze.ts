/** What a freeze holds: every product, one product, or one product's environment. */
export const FreezeScope = { Portfolio: 'portfolio', Product: 'product', Environment: 'environment' } as const;
export type FreezeScope = (typeof FreezeScope)[keyof typeof FreezeScope];

/** A hold on deploys: automatic deploys skip what it covers, and an owner's deploy needs an override with a reason. */
export interface DeployFreeze {
  readonly id: string;
  readonly scope: FreezeScope;
  /** The frozen product; null for every product. */
  readonly product: string | null;
  /** The frozen environment of the product; null for all of them. */
  readonly environment: string | null;
  readonly reason: string | null;
  readonly actor: string;
  readonly at: string;
  /** What it covers in words: `every product`, `foreverpin`, `foreverpin prod`. */
  readonly covers: string;
}

/** Pure rules of freezes. */
export const DeployFreezes = {
  /** The freeze covering a product's environment, the narrowest first; null when nothing holds deploys there. */
  covering(freezes: readonly DeployFreeze[] | undefined, product: string | null | undefined, environment: string | null | undefined): DeployFreeze | null {
    const list = freezes ?? [];
    return (
      (product && environment ? list.find((item) => item.product === product && item.environment === environment) : undefined) ??
      (product ? list.find((item) => item.product === product && item.environment === null) : undefined) ??
      list.find((item) => item.product === null) ??
      null
    );
  },

  /** The freeze of exactly one scope, or null. */
  of(freezes: readonly DeployFreeze[] | undefined, product: string | null, environment: string | null): DeployFreeze | null {
    return (freezes ?? []).find((item) => item.product === product && item.environment === environment) ?? null;
  },

  /** One line on a freeze: `Frozen (every product): launch day`. */
  describe(freeze: DeployFreeze): string {
    return `Frozen (${freeze.covers})${freeze.reason ? `: ${freeze.reason}` : ''}`;
  },
} as const;
