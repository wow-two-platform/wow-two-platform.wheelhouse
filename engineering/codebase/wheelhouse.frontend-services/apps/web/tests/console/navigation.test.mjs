import assert from "node:assert/strict";
import { after, test } from "node:test";
import { stop } from "esbuild";
import { bundleModule } from "./BundleModule.mjs";
import { product } from "./LoadModule.mjs";

const {
  scopeOf, legacyProductRedirect, productLocation, serviceLocation, vaultLocation, ConsolePaths, productTabOf, ProductTabs,
  primaryDestinationOf, PrimaryPaths,
} = await bundleModule(`
  export {
    scopeOf, legacyProductRedirect, productLocation, serviceLocation, vaultLocation, ConsolePaths, productTabOf, ProductTabs,
    primaryDestinationOf, PrimaryPaths,
  } from './src/application/navigation';
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
  // Workspaces have no page: the crumb opens the workspace's group on the Products page.
  assert.deepEqual(crumbs[1].location, { path: "/products", query: {}, hash: "#workspace-platform" });
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
  assert.deepEqual(serviceLocation("wheelhouse", "api", "prod").query, { env: "prod", inspect: "service", item: "api" });
  assert.equal(ConsolePaths.workspace("a b"), "/products#workspace-a%20b");
});

test("an old Workspace link keeps every other parameter and needs a product", () => {
  assert.deepEqual(legacyProductRedirect({ product: "foreverpin", target: "fp-dev", inspect: "target", view: ["a", "b"] }),
    { path: "/p/foreverpin", query: { target: "fp-dev", inspect: "target", view: "a" } });
  assert.equal(legacyProductRedirect({ target: "fp-dev" }), null);
  assert.equal(legacyProductRedirect({ product: "" }), null);
});

test("a product's tabs are addressed by path, Overview by the product's own address", () => {
  assert.equal(ProductTabs[0].value, "overview");
  // Releases joined Deployments (2026-10-19): one tab holds the history and the releases it can deploy.
  assert.deepEqual(ProductTabs.map((tab) => tab.label), ["Overview", "Deployments", "Secrets", "Activity"]);
  assert.equal(ConsolePaths.product("wheelhouse"), "/p/wheelhouse");
  assert.equal(ConsolePaths.product("wheelhouse", "overview"), "/p/wheelhouse");
  assert.equal(ConsolePaths.product("wheelhouse", "activity"), "/p/wheelhouse/activity");
  assert.deepEqual(productLocation("wheelhouse", { env: "prod" }, "secrets"), { path: "/p/wheelhouse/secrets", query: { env: "prod" } });
  assert.equal(productTabOf(null).value, "overview");
  assert.equal(productTabOf("deployments").label, "Deployments");
  assert.equal(productTabOf("releases"), null, "the old tab redirects at the router, not here");
  assert.equal(productTabOf("no-such-tab"), null);
});

test("a tab other than Overview closes the scope instead of the inspected service", () => {
  const crumbs = scopeOf({ ...base, path: "/p/wheelhouse/deployments", params: { product: "wheelhouse", tab: "deployments" },
    query: { env: "prod", inspect: "service", item: "api" } });
  assert.deepEqual(crumbs.map((crumb) => [crumb.level, crumb.label]),
    [["home", "Home"], ["workspace", "Platform"], ["product", "Wheelhouse"], ["tab", "Deployments"]]);
  assert.deepEqual(crumbs[3].location, { path: "/p/wheelhouse/deployments", query: {} });
  assert.equal(scopeOf({ ...base, path: "/p/wheelhouse", params: { product: "wheelhouse", tab: "overview" } }).length, 3);
});

test("every page belongs to one of the four primary destinations, or to the profile menu", () => {
  assert.deepEqual(PrimaryPaths, ["/", "/products", "/deployments", "/servers"]);
  for (const [path, destination] of [
    ["/", "/"], ["/products", "/products"], ["/w/platform", "/products"], ["/p/wheelhouse/releases", "/products"],
    ["/deployments", "/deployments"], ["/deployments/job-1", "/deployments"], ["/servers", "/servers"],
    ["/activity", null], ["/secrets", null], ["/settings/keys", null], ["/productsx", null], ["/pins", null],
  ]) assert.equal(primaryDestinationOf(path), destination, path);
});

test("a vault link opens the Secrets page on that vault and namespace", () => {
  assert.deepEqual(vaultLocation("hel1-vault"), { path: "/secrets", query: { vault: "hel1-vault" } });
  assert.deepEqual(vaultLocation("hel1-vault", "foreverpin-prod").query, { vault: "hel1-vault", namespace: "foreverpin-prod" });
});
