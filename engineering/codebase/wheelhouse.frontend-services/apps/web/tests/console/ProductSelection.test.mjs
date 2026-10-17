import assert from 'node:assert/strict';
import test from 'node:test';

import { container, loadProductModule, product, target } from './LoadModule.mjs';

const { buildProductInventory } = await loadProductModule('ProductInventory');
const { EmptyProductSelection, readProductSelection, resolveProductSelection, writeProductSelection } =
  await loadProductModule('ProductSelection');
const selectedTarget = target();
const otherTarget = target({ id: 'foreverpin-production', environment: 'production' });
const inventory = { status: 'ready', data: buildProductInventory([product()], [selectedTarget, otherTarget]) };
const baseSelection = { productKey: 'foreverpin', environment: null, targetId: selectedTarget.id, inspector: null, itemId: null };
const serviceSelection = { ...baseSelection, inspector: 'service', itemId: 'management' };
const jobSelection = { ...baseSelection, inspector: 'deployment', itemId: 'submission-id' };
const job = { id: 'submission-id', targetId: selectedTarget.id, status: 'succeeded' };
const history = { status: 'ready', data: [job] };
const vitals = { status: 'ready', data: { collectedAt: '2026-09-26T00:00:00Z',
  targets: [{ targetId: selectedTarget.id, serverId: 'rehearsal', ok: true, containers: [container()] }] } };

function resolve(requested = baseSelection, reads = {}) {
  return resolveProductSelection({ inventory, requested, ...reads });
}

test('defaults absent selections to the first actual product and target', () => {
  const result = resolve(EmptyProductSelection);
  assert.equal(result.status, 'ready');
  assert.equal(result.product.key, baseSelection.productKey);
  assert.equal(result.target, selectedTarget);
  assert.equal(result.inspector.kind, 'target');
  assert.deepEqual(result.selection, { ...baseSelection, environment: selectedTarget.environment });
});

test('resolves an explicit target inside the selected product', () => {
  const result = resolve({ ...baseSelection, targetId: otherTarget.id });
  assert.equal(result.status, 'ready');
  assert.equal(result.target, otherTarget);
});

test('does not replace unavailable explicit products or targets', () => {
  for (const [requested, issue] of [
    [{ ...baseSelection, productKey: 'removed' }, 'product-not-found'],
    [{ ...baseSelection, productKey: '' }, 'product-not-found'],
    [{ ...baseSelection, targetId: 'another-target' }, 'target-not-found'],
    [{ ...baseSelection, targetId: '' }, 'target-not-found'],
  ]) {
    const result = resolve(requested);
    assert.equal(result.status, 'unavailable');
    assert.equal(result.issue, issue);
    assert.equal(result.requested, requested);
    assert.equal(result.target, null);
  }
});

test('never selects a target belonging to another product', () => {
  const standalone = target({ id: 'other-rehearsal', product: 'other' });
  const result = resolve({ ...baseSelection, targetId: standalone.id },
    { inventory: { status: 'ready', data: buildProductInventory([product()], [selectedTarget, standalone]) } });
  assert.equal(result.status, 'unavailable');
  assert.equal(result.issue, 'target-not-found');
});

test('preserves requested identifiers while either inventory is loading or failed', () => {
  for (const status of ['loading', 'error']) {
    const result = resolve(jobSelection, { inventory: { status } });
    assert.deepEqual(result, { requested: jobSelection, product: null, target: null, status, scope: 'inventory' });
  }
});

test('resolves a service from the selected target snapshot only', () => {
  const result = resolve(serviceSelection, { vitals });
  assert.equal(result.status, 'ready');
  assert.equal(result.inspector.kind, 'service');
  assert.equal(result.inspector.service, vitals.data.targets[0].containers[0]);
  const absent = resolve({ ...serviceSelection, itemId: 'missing' }, { vitals });
  assert.equal(absent.status, 'unavailable');
  assert.equal(absent.issue, 'service-not-found');
});

test('does not claim a service exists while container readings are unresolved or unreadable', () => {
  for (const read of [undefined, { status: 'loading' }, { status: 'error' },
    { status: 'ready', data: { targets: [{ targetId: selectedTarget.id, ok: false }] } },
    { status: 'ready', data: { targets: [{ targetId: selectedTarget.id, ok: true, containers: null }] } }]) {
    const result = resolve(serviceSelection, { vitals: read });
    assert.equal(result.status, !read || read.status === 'loading' ? 'loading' : 'error');
    assert.equal(result.scope, 'service');
    assert.equal(result.requested, serviceSelection);
  }
});

test('distinguishes an empty container collection from an unreadable one', () => {
  const result = resolve(serviceSelection,
    { vitals: { status: 'ready', data: { targets: [{ targetId: selectedTarget.id, ok: true, containers: [] }] } } });
  assert.equal(result.status, 'unavailable');
  assert.equal(result.issue, 'service-not-found');
});

test('does not arbitrarily select among containers with the same service name', () => {
  const result = resolve(serviceSelection, { vitals: { status: 'ready', data: {
    targets: [{ targetId: selectedTarget.id, ok: true, containers: [container(), container()] }] } } });
  assert.equal(result.status, 'unavailable');
  assert.equal(result.issue, 'ambiguous-service');
});

test('resolves a deployment using its submission ID and validates target ownership', () => {
  const result = resolve(jobSelection, { history });
  assert.equal(result.status, 'ready');
  assert.equal(result.inspector.kind, 'deployment');
  assert.equal(result.inspector.job, job);
  const mismatch = resolve({ ...jobSelection, targetId: otherTarget.id }, { history });
  assert.equal(mismatch.status, 'unavailable');
  assert.equal(mismatch.issue, 'deployment-target-mismatch');
});

test('does not substitute the latest submission for a missing or remote rollout ID', () => {
  const result = resolve({ ...jobSelection, itemId: 'remote-rollout-id' }, { history });
  assert.equal(result.status, 'unavailable');
  assert.equal(result.issue, 'deployment-not-found');
});

test('waits for deployment history without losing the requested submission', () => {
  for (const status of ['loading', 'error']) {
    const result = resolve(jobSelection, { history: { status } });
    assert.equal(result.status, status);
    assert.equal(result.scope, 'deployment');
    assert.equal(result.requested, jobSelection);
  }
  assert.equal(resolve(jobSelection).status, 'loading');
});

test('rejects ambiguous submission IDs', () => {
  const result = resolve(jobSelection, { history: { status: 'ready', data: [job, { ...job }] } });
  assert.equal(result.status, 'unavailable');
  assert.equal(result.issue, 'ambiguous-deployment');
});

test('keeps empty and unconfigured inventory states ready without inventing a target', () => {
  const empty = resolve(EmptyProductSelection, { inventory: { status: 'ready', data: { products: [] } } });
  assert.equal(empty.status, 'ready');
  assert.equal(empty.product, null);
  assert.equal(empty.target, null);
  assert.equal(empty.inspector, null);
  const unconfigured = resolve(EmptyProductSelection,
    { inventory: { status: 'ready', data: buildProductInventory([product()], []) } });
  assert.equal(unconfigured.status, 'ready');
  assert.equal(unconfigured.product.key, baseSelection.productKey);
  assert.equal(unconfigured.target, null);
});

test('rejects an explicit inspector when no target exists', () => {
  const result = resolve({ ...serviceSelection, targetId: null },
    { inventory: { status: 'ready', data: buildProductInventory([product()], []) } });
  assert.equal(result.status, 'unavailable');
  assert.equal(result.issue, 'target-required');
});

test('rejects unknown inspectors and missing or extraneous inspector identifiers', () => {
  for (const requested of [
    { ...baseSelection, inspector: 'logs' },
    { ...baseSelection, itemId: 'orphan' },
    { ...baseSelection, inspector: 'target', itemId: 'extraneous' },
    { ...baseSelection, inspector: 'service' },
    { ...baseSelection, inspector: 'service', itemId: '' },
    { ...baseSelection, inspector: 'deployment' },
  ]) {
    const result = resolve(requested);
    assert.equal(result.status, 'unavailable');
    assert.equal(result.issue, 'invalid-inspector');
  }
});

test('rejects ambiguous explicit product and target identifiers', () => {
  const entry = inventory.data.products[0];
  const duplicateProduct = resolve(baseSelection,
    { inventory: { status: 'ready', data: { products: [entry, entry] } } });
  assert.equal(duplicateProduct.status, 'unavailable');
  assert.equal(duplicateProduct.issue, 'ambiguous-product');
  const duplicateTarget = resolve(baseSelection, { inventory: { status: 'ready', data: {
    products: [{ ...entry, targets: [selectedTarget, selectedTarget] }] } } });
  assert.equal(duplicateTarget.status, 'unavailable');
  assert.equal(duplicateTarget.issue, 'ambiguous-target');
});

test('URL round-trips literal identifiers and preserves explicit empty parameters', () => {
  const value = { productKey: 'runner:forever-pin', environment: 'dev', targetId: null, inspector: 'service',
    itemId: 'worker+queue' };
  assert.deepEqual(readProductSelection(value.productKey, writeProductSelection(new URLSearchParams(), value)), value);
  assert.deepEqual(readProductSelection('', new URLSearchParams('env=&target=&inspect=&item=')),
    { productKey: '', environment: '', targetId: '', inspector: '', itemId: '' });
});

test('URL updates preserve unrelated parameters without mutating the original search', () => {
  const original = new URLSearchParams('view=activity&env=old&target=old&inspect=service&item=old');
  const previous = original.toString();
  const updated = writeProductSelection(original, EmptyProductSelection);
  assert.equal(updated.toString(), 'view=activity');
  assert.equal(original.toString(), previous);
});

test('selects a target by its environment, and never another environment for a missing one', () => {
  const byEnvironment = resolve({ ...baseSelection, targetId: null, environment: otherTarget.environment });
  assert.equal(byEnvironment.status, 'ready');
  assert.equal(byEnvironment.target, otherTarget);
  assert.equal(byEnvironment.selection.environment, otherTarget.environment);
  const missing = resolve({ ...baseSelection, targetId: null, environment: 'staging' });
  assert.equal(missing.status, 'unavailable');
  assert.equal(missing.issue, 'target-not-found');
  assert.equal(missing.target, null);
});

test('names an old link\'s literal target by its environment and drops the target parameter', () => {
  const result = resolve({ ...baseSelection, targetId: otherTarget.id, inspector: 'target' });
  assert.equal(result.status, 'ready');
  assert.equal(result.selection.environment, otherTarget.environment);
  const params = writeProductSelection(new URLSearchParams(`target=${otherTarget.id}&inspect=target`), result.selection);
  assert.deepEqual(Object.fromEntries(params), { env: otherTarget.environment, inspect: 'target' });
});

test('refuses to choose between two targets of one environment', () => {
  const twin = target({ id: 'foreverpin-rehearsal-2' });
  const result = resolve({ ...baseSelection, targetId: null, environment: selectedTarget.environment },
    { inventory: { status: 'ready', data: buildProductInventory([product()], [selectedTarget, twin]) } });
  assert.equal(result.status, 'unavailable');
  assert.equal(result.issue, 'ambiguous-target');
});

test('resolution leaves requested values and inventory records untouched', () => {
  const requested = Object.freeze({ ...serviceSelection });
  const before = JSON.stringify({ inventory, vitals, requested });
  resolve(requested, { vitals });
  assert.equal(JSON.stringify({ inventory, vitals, requested }), before);
});
