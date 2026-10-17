import assert from "node:assert/strict";
import { after, test } from "node:test";
import { stop } from "esbuild";
import { bundleModule } from "./BundleModule.mjs";
import { container, product, target } from "./LoadModule.mjs";

const { groupPortfolio, workspaceOf, browse, workspaceChips, ProductStatusRules, EnvironmentHealthRules, Ordering } = await bundleModule(`
  export { groupPortfolio, workspaceOf } from './src/application/portfolio/Portfolio';
  export { browse, workspaceChips } from './src/application/portfolio/ProductBrowser';
  export { ProductStatusRules } from './src/domain/products/ProductStatusRules';
  export { EnvironmentHealthRules } from './src/domain/overview/EnvironmentHealth';
  export { Ordering } from './src/domain/common/Ordering';
`);
after(() => stop());

const platform = { id: "w-platform", slug: "platform", name: "Platform", description: "", order: 0 };
const ventures = { id: "w-ventures", slug: "ventures", name: "Ventures", description: "", order: 1 };
const wheelhouse = product({ id: "p-wh", slug: "wheelhouse", name: "Wheelhouse", workspaceId: platform.id });
const vault = product({ id: "p-vault", slug: "vault", name: "Secrets Vault", workspaceId: platform.id, status: "inactive" });
const pin = product({ id: "p-pin", slug: "foreverpin", name: "ForeverPin", workspaceId: ventures.id });
const stray = product({ id: "p-stray", slug: "stray", name: "Stray", workspaceId: "w-gone" });

test("groups products under their workspaces in custom order, by name, hiding inactive ones", () => {
  const groups = groupPortfolio([ventures, platform], [pin, vault, wheelhouse], false);
  assert.deepEqual(groups.map((group) => group.workspace.slug), ["platform", "ventures"]);
  assert.deepEqual(groups[0].products.map((item) => item.slug), ["wheelhouse"]);
  assert.equal(groups[0].total, 2);
  assert.equal(groups[0].active, 1);
});

test("Show inactive reveals inactive products in their place", () => {
  const [platformGroup] = groupPortfolio([platform], [wheelhouse, vault], true);
  assert.deepEqual(platformGroup.products.map((item) => item.slug), ["vault", "wheelhouse"]);
});

test("an empty workspace still shows, and unknown workspaces gather last without one", () => {
  const groups = groupPortfolio([platform, ventures], [wheelhouse, stray, product({ slug: "loose", workspaceId: null })], false);
  assert.deepEqual(groups.map((group) => group.workspace?.slug ?? null), ["platform", "ventures", null]);
  assert.equal(groups[1].products.length, 0);
  assert.deepEqual(groups[2].products.map((item) => item.slug), ["loose", "stray"]);
  assert.equal(workspaceOf([platform], wheelhouse), platform);
  assert.equal(workspaceOf([platform], stray), null);
});

test("status filter keeps every item while inactive ones show, else only active ones", () => {
  const items = [wheelhouse, vault, pin];
  assert.deepEqual(ProductStatusRules.visible(items, ProductStatusRules.isActive, false), [wheelhouse, pin]);
  assert.deepEqual(ProductStatusRules.visible(items, ProductStatusRules.isActive, true), items);
  const withWorker = { ...wheelhouse, inactiveServices: ["worker"] };
  const services = ["api", "worker"];
  assert.deepEqual(ProductStatusRules.visible(services, (name) => ProductStatusRules.isServiceActive(withWorker, name), false), ["api"]);
  assert.deepEqual(ProductStatusRules.visible(services, (name) => ProductStatusRules.isServiceActive(withWorker, name), true), services);
});

test("optimistic status patches change only what was asked and keep inactive services sorted", () => {
  assert.equal(ProductStatusRules.withStatus(wheelhouse, "active"), wheelhouse);
  assert.equal(ProductStatusRules.withStatus(wheelhouse, "inactive").status, "inactive");
  const one = ProductStatusRules.withServiceStatus(wheelhouse, "worker", "inactive");
  const two = ProductStatusRules.withServiceStatus(one, "api", "inactive");
  assert.deepEqual(two.inactiveServices, ["api", "worker"]);
  assert.deepEqual(ProductStatusRules.withServiceStatus(two, "worker", "active").inactiveServices, ["api"]);
  assert.equal(ProductStatusRules.withWorkspace(wheelhouse, ventures.id).workspaceId, ventures.id);
});

test("a product reference names it by row identifier or slug, and a record prefers the identifier", () => {
  assert.equal(ProductStatusRules.isProduct(wheelhouse, "p-wh"), true);
  assert.equal(ProductStatusRules.isProduct(wheelhouse, "wheelhouse"), true);
  assert.equal(ProductStatusRules.isProduct(wheelhouse, "vault"), false);
  assert.equal(ProductStatusRules.isProduct(wheelhouse, null), false);
  assert.equal(ProductStatusRules.referenceOf(wheelhouse), "p-wh");
  assert.equal(ProductStatusRules.referenceOf({ slug: "wheelhouse" }), "wheelhouse");
});

test("custom order sorts by place, moves within bounds and applies a saved id list", () => {
  const items = [{ id: "b", order: 1 }, { id: "a", order: 0 }, { id: "c", order: 2 }];
  assert.deepEqual(Ordering.ids(items), ["a", "b", "c"]);
  assert.deepEqual(Ordering.move(["a", "b", "c"], 0, 2), ["b", "c", "a"]);
  assert.deepEqual(Ordering.move(["a", "b", "c"], 2, 5), ["a", "b", "c"]);
  assert.deepEqual(Ordering.move(["a", "b", "c"], -1, 0), ["a", "b", "c"]);
  assert.deepEqual(Ordering.apply(items, ["c", "a", "gone"]).map((item) => [item.id, item.order]),
    [["c", 0], ["a", 1], ["b", 2]]);
});

test("environment health reads targets, vitals and stats, worst first, and marks missing environments", () => {
  const dev = target({ id: "fp-dev", environment: "dev" });
  const prod = target({ id: "fp-prod", environment: "prod" });
  const vitals = { collectedAt: "2026-10-17T00:00:00Z", servers: [], targets: [
    { targetId: dev.id, serverId: "s", ok: true, condition: "ready", release: "v1", containers: [container()] },
    { targetId: prod.id, serverId: "s", ok: true, condition: "ready", release: "v1",
      containers: [container({ state: "exited", health: null })] },
  ] };
  const readings = EnvironmentHealthRules.ofProduct("foreverpin", [dev, prod], { vitals });
  assert.deepEqual(readings.map((item) => [item.environment, item.health]),
    [["dev", "healthy"], ["prod", "failing"]]);
  assert.deepEqual(EnvironmentHealthRules.ofProduct("foreverpin", [prod], { vitals }).map((item) => [item.environment, item.health]),
    [["dev", "missing"], ["prod", "failing"]]);
  assert.deepEqual(EnvironmentHealthRules.ofProduct("foreverpin", [dev], { vitals: undefined })[0].health, "unknown");
  const stats = { targets: [{ targetId: dev.id, failingSince: "2026-10-16T00:00:00Z" }] };
  assert.equal(EnvironmentHealthRules.ofTarget(dev, { vitals, stats }), "failing");
  const idle = { ...vitals, targets: [{ targetId: dev.id, serverId: "s", ok: true, condition: "ready", release: null, containers: [] }] };
  assert.equal(EnvironmentHealthRules.ofTarget(dev, { vitals: idle }), "idle");
  const history = [{ id: "new", targetId: "other" }, { id: "mine", targetId: dev.id }, { id: "old", targetId: prod.id }];
  assert.equal(EnvironmentHealthRules.lastDeploy([dev.id, prod.id], history).id, "mine");
  assert.equal(EnvironmentHealthRules.lastDeploy([dev.id], undefined), null);
});

test("the browser's chips are All and each workspace in order, counting the products they show", () => {
  const hidden = groupPortfolio([ventures, platform], [pin, vault, wheelhouse, stray], false);
  assert.deepEqual(workspaceChips(hidden).map((chip) => [chip.label, chip.count, chip.path]),
    [["All", 3, "/products"], ["Platform", 1, "/w/platform"], ["Ventures", 1, "/w/ventures"]]);
  const shown = groupPortfolio([ventures, platform], [pin, vault, wheelhouse, stray], true);
  assert.deepEqual(workspaceChips(shown).map((chip) => chip.count), [4, 2, 1]);
});

test("the browser shows every workspace for All, one for a chosen workspace, and none for an unknown one", () => {
  const groups = groupPortfolio([ventures, platform], [pin, wheelhouse], false);
  assert.equal(browse(groups, null).groups.length, 2);
  assert.deepEqual(browse(groups, "ventures").groups.map((group) => group.workspace.slug), ["ventures"]);
  assert.deepEqual(browse(groups, "gone"), { groups: [], missing: true });
});
