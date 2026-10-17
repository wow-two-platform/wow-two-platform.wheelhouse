import assert from "node:assert/strict";
import { after, test } from "node:test";
import { stop } from "esbuild";
import { bundleModule } from "./BundleModule.mjs";
import { product } from "./LoadModule.mjs";

const { scopeOf, legacyProductRedirect, productLocation, serviceLocation, ConsolePaths } = await bundleModule(`
  export { scopeOf } from './src/application/navigation/Scope';
  export { legacyProductRedirect, productLocation, serviceLocation, ConsolePaths } from './src/application/navigation/ConsolePaths';
`);
after(() => stop());

const platform = { id: "w-platform", slug: "platform", name: "Platform", description: "", order: 0 };
const wheelhouse = product({ id: "p-wh", slug: "wheelhouse", name: "Wheelhouse", workspaceId: platform.id });
const base = { params: {}, query: {}, title: null, workspaces: [platform], products: [wheelhouse] };

test("the scope names Home, the workspace, the product and the inspected service", () => {
  const crumbs = scopeOf({ ...base, path: "/p/wheelhouse", params: { product: "wheelhouse" },
    query: { env: "prod", inspect: "service", item: "api" } });
  assert.deepEqual(crumbs.map((crumb) => [crumb.level, crumb.label]),
    [["home", "Home"], ["workspace", "Platform"], ["product", "Wheelhouse"], ["service", "api"]]);
  assert.deepEqual(crumbs[1].location, { path: "/w/platform", query: {} });
  assert.deepEqual(crumbs[2].location, { path: "/p/wheelhouse", query: { env: "prod" } });
  assert.deepEqual(crumbs[3].location.query, { env: "prod", inspect: "service", item: "api" });
});

test("before the inventory arrives the scope shows the slugs from the address", () => {
  const crumbs = scopeOf({ ...base, workspaces: [], products: [], path: "/p/wheelhouse", params: { product: "wheelhouse" } });
  assert.deepEqual(crumbs.map((crumb) => crumb.label), ["Home", "wheelhouse"]);
  assert.deepEqual(scopeOf({ ...base, workspaces: [], path: "/w/platform", params: { workspace: "platform" } })
    .map((crumb) => crumb.label), ["Home", "platform"]);
});

test("tool pages sit under Home with their own title, and Home alone is Home", () => {
  assert.deepEqual(scopeOf({ ...base, path: "/deployments", title: "Deployments" }).map((crumb) => crumb.label),
    ["Home", "Deployments"]);
  assert.deepEqual(scopeOf({ ...base, path: "/" }).map((crumb) => crumb.label), ["Home"]);
});

test("links carry only the parameters they need", () => {
  assert.deepEqual(productLocation("wheelhouse"), { path: "/p/wheelhouse", query: {} });
  assert.deepEqual(serviceLocation("wheelhouse", "api", null), { path: "/p/wheelhouse", query: { inspect: "service", item: "api" } });
  assert.deepEqual(serviceLocation("wheelhouse", "api", "test").query, { env: "test", inspect: "service", item: "api" });
  assert.equal(ConsolePaths.workspace("a b"), "/w/a%20b");
});

test("an old Workspace link keeps every other parameter and needs a product", () => {
  assert.deepEqual(legacyProductRedirect({ product: "foreverpin", target: "fp-dev", inspect: "target", view: ["a", "b"] }),
    { path: "/p/foreverpin", query: { target: "fp-dev", inspect: "target", view: "a" } });
  assert.equal(legacyProductRedirect({ target: "fp-dev" }), null);
  assert.equal(legacyProductRedirect({ product: "" }), null);
});
