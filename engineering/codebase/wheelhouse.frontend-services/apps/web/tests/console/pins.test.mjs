import assert from "node:assert/strict";
import { after, test } from "node:test";
import { stop } from "esbuild";
import { bundleModule } from "./BundleModule.mjs";
import { product } from "./LoadModule.mjs";

const { PinRules, resolvePins } = await bundleModule(`
  export { PinRules } from './src/domain/pins/PinRules';
  export { resolvePins } from './src/application/pins/PinnedEntries';
`);
after(() => stop());

const wheelhouse = product({ id: "p-wh", slug: "wheelhouse", name: "Wheelhouse" });
const foreverPin = product({ id: "p-fp", slug: "foreverpin", name: "ForeverPin", status: "inactive" });
const pin = (id, order, fields) => ({ id, order, kind: "product", workspaceId: null, productId: null, service: null, ...fields });
const pins = [
  pin("c", 2, { productId: "p-fp" }),
  pin("a", 0, { productId: "wheelhouse" }),
  // Made before pins became products only; the host converts these, and the console leaves any it still sees out.
  pin("b", 1, { kind: "workspace", workspaceId: "w-platform" }),
];

test("a pin matches a product by row identifier or slug, and only a product pin matches", () => {
  assert.equal(PinRules.find(pins, { kind: "product", product: wheelhouse }).id, "a");
  assert.equal(PinRules.find(pins, { kind: "product", product: { slug: "foreverpin", id: "p-fp" } }).id, "c");
  assert.equal(PinRules.find(pins, { kind: "product", product: { slug: "other" } }), undefined);
  assert.equal(PinRules.find([pin("s", 0, { kind: "service", productId: "p-wh", service: "api" })],
    { kind: "product", product: wheelhouse }), undefined);
});

test("only product pins count, in custom order", () => {
  assert.deepEqual(PinRules.products(pins).map((item) => item.id), ["a", "c"]);
});

test("pinning sends the product's row identifier, or its slug when the host sends none", () => {
  assert.deepEqual(PinRules.requestOf({ kind: "product", product: wheelhouse }), { kind: "product", productId: "p-wh" });
  assert.deepEqual(PinRules.requestOf({ kind: "product", product: { slug: "wheelhouse" } }),
    { kind: "product", productId: "wheelhouse" });
});

test("optimistic pin and unpin keep the custom order, numbered from zero, and never pin twice", () => {
  const list = PinRules.products(pins);
  const added = PinRules.withPin(list, { kind: "product", product: { slug: "haven" } });
  assert.deepEqual(added.map((item) => item.id).slice(0, 2), ["a", "c"]);
  assert.equal(added[2].order, 2);
  assert.equal(PinRules.isPending(added[2]), true);
  assert.equal(PinRules.withPin(list, { kind: "product", product: wheelhouse }).length, 2);
  assert.deepEqual(PinRules.withoutPin(list, "a").map((item) => [item.id, item.order]), [["c", 0]]);
});

test("a reorder sends every pin id once in the new order", () => {
  const list = [pin("a", 0), pin("b", 1), pin("c", 2)];
  assert.deepEqual(PinRules.reorder(list, 2, 0), ["c", "a", "b"]);
  assert.deepEqual(PinRules.reorder(list, 0, 1), ["b", "a", "c"]);
  assert.deepEqual(PinRules.reorder(list, 0, 9), ["a", "b", "c"]);
});

test("pinned products resolve in custom order to their pages; an inactive one shows dimmed", () => {
  const entries = resolvePins(pins, [wheelhouse, foreverPin]);
  assert.deepEqual(entries.map((entry) => entry.label), ["Wheelhouse", "ForeverPin"]);
  assert.deepEqual(entries[0].location, { path: "/p/wheelhouse", query: {} });
  assert.equal(entries[0].product, wheelhouse);
  assert.equal(entries[0].active, true);
  assert.equal(entries[1].active, false);
});

test("a pin whose product is gone stays for unpinning", () => {
  const [entry] = resolvePins([pin("x", 0, { productId: "p-gone" })], [wheelhouse]);
  assert.equal(entry.location, null);
  assert.equal(entry.product, null);
  assert.equal(entry.label, "Removed product");
});
