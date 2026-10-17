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

const platform = { id: "w-platform", slug: "platform", name: "Platform", description: "", order: 0 };
const wheelhouse = product({ id: "p-wh", slug: "wheelhouse", name: "Wheelhouse", workspaceId: platform.id,
  inactiveServices: ["worker"] });
const pin = (id, order, fields) => ({ id, order, kind: "product", workspaceId: null, productId: null, service: null, ...fields });
const pins = [
  pin("c", 2, { kind: "service", productId: "p-wh", service: "api" }),
  pin("a", 0, { kind: "workspace", workspaceId: platform.id }),
  pin("b", 1, { productId: "wheelhouse" }),
];

test("a pin matches its subject by kind, and a product by row identifier or slug", () => {
  assert.equal(PinRules.find(pins, { kind: "workspace", workspaceId: platform.id }).id, "a");
  assert.equal(PinRules.find(pins, { kind: "product", product: wheelhouse }).id, "b");
  assert.equal(PinRules.find(pins, { kind: "service", product: wheelhouse, service: "api" }).id, "c");
  assert.equal(PinRules.find(pins, { kind: "service", product: wheelhouse, service: "web" }), undefined);
  assert.equal(PinRules.find(pins, { kind: "product", product: { slug: "other" } }), undefined);
});

test("pinning sends the product's row identifier, or its slug when the host sends none", () => {
  assert.deepEqual(PinRules.requestOf({ kind: "service", product: wheelhouse, service: "api" }),
    { kind: "service", productId: "p-wh", service: "api" });
  assert.deepEqual(PinRules.requestOf({ kind: "product", product: { slug: "wheelhouse" } }),
    { kind: "product", productId: "wheelhouse" });
  assert.deepEqual(PinRules.requestOf({ kind: "workspace", workspaceId: "w-platform" }),
    { kind: "workspace", workspaceId: "w-platform" });
});

test("optimistic pin and unpin keep the custom order, numbered from zero, and never pin twice", () => {
  const added = PinRules.withPin(pins, { kind: "service", product: wheelhouse, service: "web" });
  assert.deepEqual(added.map((item) => item.id).slice(0, 3), ["a", "b", "c"]);
  assert.equal(added[3].order, 3);
  assert.equal(PinRules.isPending(added[3]), true);
  assert.equal(PinRules.withPin(pins, { kind: "workspace", workspaceId: platform.id }).length, 3);
  assert.deepEqual(PinRules.withoutPin(pins, "b").map((item) => [item.id, item.order]), [["a", 0], ["c", 1]]);
});

test("a reorder sends every pin id once in the new order", () => {
  assert.deepEqual(PinRules.reorder(pins, 2, 0), ["c", "a", "b"]);
  assert.deepEqual(PinRules.reorder(pins, 0, 1), ["b", "a", "c"]);
  assert.deepEqual(PinRules.reorder(pins, 0, 9), ["a", "b", "c"]);
});

test("pins resolve in custom order to their pages; a service opens in the environment last chosen", () => {
  const entries = resolvePins(pins, [platform], [wheelhouse], "prod");
  assert.deepEqual(entries.map((entry) => entry.label), ["Platform", "Wheelhouse", "api"]);
  assert.deepEqual(entries[0].location, { path: "/w/platform", query: {} });
  assert.deepEqual(entries[1].location, { path: "/p/wheelhouse", query: {} });
  assert.deepEqual(entries[2].location, { path: "/p/wheelhouse", query: { env: "prod", inspect: "service", item: "api" } });
  assert.equal(entries[2].detail, "Wheelhouse");
  assert.deepEqual(resolvePins([pins[0]], [platform], [wheelhouse], null)[0].location.query, { inspect: "service", item: "api" });
});

test("a pin whose target is gone stays for unpinning, and pins to inactive things show dimmed", () => {
  const entries = resolvePins([pin("x", 0, { productId: "p-gone" }), pin("y", 1, { kind: "service", productId: "p-wh", service: "worker" })],
    [platform], [wheelhouse], null);
  assert.equal(entries[0].location, null);
  assert.equal(entries[0].label, "Removed item");
  assert.equal(entries[1].active, false);
});
