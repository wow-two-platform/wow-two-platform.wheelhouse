import assert from 'node:assert/strict';
import test from 'node:test';

import { loadProductModule, product, target } from './LoadModule.mjs';

const { buildProductInventory } = await loadProductModule('ProductInventory');

test('keys each catalog product by its slug and gathers the targets that name it', () => {
  const cataloged = product();
  const binding = target();
  const { products } = buildProductInventory([cataloged], [binding]);
  assert.equal(products.length, 1);
  assert.equal(products[0].key, 'foreverpin');
  assert.equal(products[0].product, cataloged);
  assert.deepEqual(products[0].targets, [binding]);
});

test('keeps catalog order and a product with no target', () => {
  const { products } = buildProductInventory([product({ slug: 'zeta', name: 'Zeta' }), product()], [target()]);
  assert.deepEqual(products.map((entry) => [entry.key, entry.targets.length]), [['zeta', 0], ['foreverpin', 1]]);
});

test('lists targets whose product the catalog lacks after the catalog, by their literal product', () => {
  const { products } = buildProductInventory([product()], [target(),
    target({ id: 'ghost-dev', product: 'ghost' }), target({ id: 'case', product: 'ForeverPin' })]);
  assert.deepEqual(products.map((entry) => [entry.key, entry.product === null]),
    [['foreverpin', false], ['ghost', true], ['ForeverPin', true]]);
});

test('keeps every target available when the catalog read is empty', () => {
  const { products } = buildProductInventory([], [target()]);
  assert.deepEqual(products.map((entry) => [entry.key, entry.product]), [['foreverpin', null]]);
});

test('groups several environments of one product in target order', () => {
  const production = target({ id: 'foreverpin-production', environment: 'production' });
  const { products } = buildProductInventory([product()], [target(), production]);
  assert.deepEqual(products[0].targets.map((entry) => entry.id), ['foreverpin-rehearsal', 'foreverpin-production']);
});

test('does not mutate catalog records, targets or arrays', () => {
  const catalog = Object.freeze([Object.freeze(product())]);
  const targets = Object.freeze([Object.freeze(target())]);
  const before = JSON.stringify({ catalog, targets });
  buildProductInventory(catalog, targets);
  assert.equal(JSON.stringify({ catalog, targets }), before);
});
