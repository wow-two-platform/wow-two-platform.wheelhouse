import type { DeploymentJob, DeploymentTarget } from '@/domain/deployments';
import type { ContainerVitals, ServerVitals } from '@/domain/servers';

import type { InventoryProduct, ProductInventory } from './ProductInventory';

/**
 * Represents the requested selection on a product page; null means absent, while an empty string remains explicit.
 * The product comes from the path, the environment from `?env=`; `targetId` carries an old link's `?target=` until the
 * page rewrites it as the environment.
 */
export interface ProductSelection {
  readonly productKey: string | null;
  readonly environment: string | null;
  readonly targetId: string | null;
  readonly inspector: string | null;
  readonly itemId: string | null;
}

/** Defines a completed inventory read or an unresolved loading/error state. */
export type SelectionRead<T> =
  | { readonly status: 'ready'; readonly data: T }
  | { readonly status: 'loading' }
  | { readonly status: 'error' };

/** Represents an inspector anchored to an observed target, service or submission. */
export type ProductInspector =
  | { readonly kind: 'target'; readonly target: DeploymentTarget }
  | { readonly kind: 'service'; readonly target: DeploymentTarget; readonly service: ContainerVitals }
  | { readonly kind: 'deployment'; readonly target: DeploymentTarget; readonly job: DeploymentJob };

/** Defines the reads needed to resolve a requested product selection. */
export interface ProductSelectionInput {
  readonly requested: ProductSelection;
  readonly inventory: SelectionRead<ProductInventory>;
  readonly vitals?: SelectionRead<ServerVitals>;
  readonly history?: SelectionRead<readonly DeploymentJob[]>;
}

/** Defines why an explicit selection cannot resolve within its requested scope. */
export type ProductSelectionIssue =
  | 'product-not-found' | 'ambiguous-product' | 'target-not-found' | 'ambiguous-target'
  | 'target-required' | 'invalid-inspector' | 'service-not-found' | 'ambiguous-service'
  | 'deployment-not-found' | 'ambiguous-deployment' | 'deployment-target-mismatch';

/** Represents a resolved selection or the exact scope that remains unavailable. */
export type ProductSelectionResult = {
  readonly requested: ProductSelection;
  readonly product: InventoryProduct | null;
  readonly target: DeploymentTarget | null;
} & (
  | { readonly status: 'ready'; readonly selection: ProductSelection; readonly inspector: ProductInspector | null }
  | { readonly status: 'loading' | 'error'; readonly scope: 'inventory' | 'service' | 'deployment' }
  | { readonly status: 'unavailable'; readonly issue: ProductSelectionIssue }
);

/** Represents a visit without an explicit product, environment or inspector. */
export const EmptyProductSelection: ProductSelection = {
  productKey: null, environment: null, targetId: null, inspector: null, itemId: null,
};

/** Reads literal selection identifiers without normalizing or discarding invalid explicit values. */
export function readProductSelection(productKey: string | null, search: URLSearchParams): ProductSelection {
  return { productKey, environment: search.get('env'), targetId: search.get('target'),
    inspector: search.get('inspect'), itemId: search.get('item') };
}

/**
 * Returns updated selection parameters while preserving unrelated parameters and the original instance. The
 * environment is written as `env`; an old `target` parameter is dropped, since the environment now names it.
 */
export function writeProductSelection(search: URLSearchParams, selection: ProductSelection): URLSearchParams {
  const updated = new URLSearchParams(search);
  updated.delete('target');
  const values = [['env', selection.environment], ['inspect', selection.inspector], ['item', selection.itemId]] as const;
  for (const [key, value] of values) {
    if (value === null) updated.delete(key);
    else updated.set(key, value);
  }
  return updated;
}

/** The targets a request names: an old link's literal target, else the environment, else the first. @internal */
function requestedTargets(product: InventoryProduct | null, requested: ProductSelection): readonly DeploymentTarget[] {
  const targets = product?.targets ?? [];
  if (requested.targetId !== null) return targets.filter((item) => item.id === requested.targetId);
  if (requested.environment !== null) return targets.filter((item) => item.environment === requested.environment);
  return targets.slice(0, 1);
}

/** Resolves defaults only for absent parameters; pending reads and invalid explicit identifiers never switch scope. */
export function resolveProductSelection(input: ProductSelectionInput): ProductSelectionResult {
  const { requested, inventory } = input;
  const empty = { requested, product: null, target: null };
  if (inventory.status !== 'ready') return { ...empty, status: inventory.status, scope: 'inventory' };

  const products = requested.productKey === null
    ? inventory.data.products.slice(0, 1)
    : inventory.data.products.filter((item) => item.key === requested.productKey);
  const product = products[0] ?? null;
  if (products.length > 1) return { ...empty, status: 'unavailable', issue: 'ambiguous-product' };
  if (!product && requested.productKey !== null)
    return { ...empty, status: 'unavailable', issue: 'product-not-found' };

  const targets = requestedTargets(product, requested);
  const target = targets[0] ?? null;
  const context = { requested, product, target };
  if (targets.length > 1) return { ...context, target: null, status: 'unavailable', issue: 'ambiguous-target' };
  if (!target && (requested.targetId !== null || requested.environment !== null))
    return { ...context, status: 'unavailable', issue: 'target-not-found' };

  const selection = { ...requested, productKey: product?.key ?? null, environment: target?.environment ?? null,
    targetId: target?.id ?? null };
  if (requested.inspector === null && requested.itemId === null)
    return { ...context, status: 'ready', selection, inspector: target ? { kind: 'target', target } : null };
  if (requested.inspector !== 'target' && requested.inspector !== 'service' && requested.inspector !== 'deployment')
    return { ...context, status: 'unavailable', issue: 'invalid-inspector' };
  if (!target) return { ...context, status: 'unavailable', issue: 'target-required' };
  if (requested.inspector === 'target') {
    if (requested.itemId !== null) return { ...context, status: 'unavailable', issue: 'invalid-inspector' };
    return { ...context, status: 'ready', selection, inspector: { kind: 'target', target } };
  }
  if (requested.itemId === null || requested.itemId === '')
    return { ...context, status: 'unavailable', issue: 'invalid-inspector' };

  if (requested.inspector === 'service') {
    const read = input.vitals ?? { status: 'loading' };
    if (read.status !== 'ready') return { ...context, status: read.status, scope: 'service' };
    const readings = read.data.targets.filter((item) => item.targetId === target.id);
    const reading = readings[0];
    if (readings.length !== 1 || !reading?.ok || reading.containers == null)
      return { ...context, status: 'error', scope: 'service' };
    const services = reading.containers.filter((item) => item.service === requested.itemId);
    const service = services[0];
    if (!service) return { ...context, status: 'unavailable', issue: 'service-not-found' };
    if (services.length !== 1) return { ...context, status: 'unavailable', issue: 'ambiguous-service' };
    return { ...context, status: 'ready', selection, inspector: { kind: 'service', target, service } };
  }

  const read = input.history ?? { status: 'loading' };
  if (read.status !== 'ready') return { ...context, status: read.status, scope: 'deployment' };
  const jobs = read.data.filter((item) => item.id === requested.itemId);
  const job = jobs[0];
  if (!job) return { ...context, status: 'unavailable', issue: 'deployment-not-found' };
  if (jobs.length !== 1) return { ...context, status: 'unavailable', issue: 'ambiguous-deployment' };
  if (job.targetId !== target.id)
    return { ...context, status: 'unavailable', issue: 'deployment-target-mismatch' };
  return { ...context, status: 'ready', selection, inspector: { kind: 'deployment', target, job } };
}
