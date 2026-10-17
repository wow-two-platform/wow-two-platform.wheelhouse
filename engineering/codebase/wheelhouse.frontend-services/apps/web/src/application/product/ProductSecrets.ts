import type { Product } from '@/domain/products';
import { environmentRank } from '@/domain/targets';

/** Where one environment of a product keeps its secrets: a namespace in its server's vault, or none. */
export interface SecretBinding {
  readonly environment: string;
  /** The vault's slug; null when the environment's server runs no vault. */
  readonly vault: string | null;
  readonly namespace: string | null;
}

/** A product's environments in promotion order, each with the vault namespace its settings belong in. */
export function secretBindingsOf(product: Product | null | undefined): SecretBinding[] {
  return [...(product?.environments ?? [])]
    .sort((left, right) => environmentRank(left.name) - environmentRank(right.name))
    .map((environment) => ({
      environment: environment.name,
      vault: environment.secrets?.vault ?? null,
      namespace: environment.secrets?.namespace ?? null,
    }));
}

/**
 * The environment the Secrets tab opens: the one the address names when the product has it, else the one last chosen
 * on any product page, else the first with a vault, else the first. Null for a product without environments.
 */
export function secretEnvironmentOf(
  bindings: readonly SecretBinding[],
  requested: string | null,
  remembered: string | null,
): SecretBinding | null {
  return (
    bindings.find((binding) => binding.environment === requested) ??
    bindings.find((binding) => binding.environment === remembered) ??
    bindings.find((binding) => binding.vault !== null) ??
    bindings[0] ??
    null
  );
}
