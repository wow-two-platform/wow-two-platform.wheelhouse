import assert from "node:assert/strict";
import { after, test } from "node:test";
import { stop } from "esbuild";
import { bundleModule } from "./BundleModule.mjs";
import { product } from "./LoadModule.mjs";

const {
  EnvironmentPillRules, shortRelease, factsOf, conditionOf, rolloutConditionOf, ProductFilterQuery, ProductFilterRules,
  DefaultProductFilter, workspaceStatsOf, siteLinksOf, directSiteOf, productsDestination, workspaceOfHash, ConsolePaths,
} = await bundleModule(`
  export {
    EnvironmentPillRules, shortRelease, factsOf, conditionOf, rolloutConditionOf, ProductFilterQuery, ProductFilterRules,
    DefaultProductFilter, workspaceStatsOf, siteLinksOf, directSiteOf,
  } from './src/domain/portfolio';
  export { productsDestination, workspaceOfHash, ConsolePaths } from './src/application/navigation';
`);
after(() => stop());

const deploy = (fields) => ({
  id: "d", release: "v1.2.0", status: "succeeded", outcome: "succeeded", actor: "max",
  submittedAt: "2026-10-06T10:00:00+00:00", completedAt: "2026-10-06T10:03:00+00:00", reason: null, ...fields,
});
const reading = (health) => ({ environment: "prod", health, targetId: health === "missing" ? null : "fp-prod" });
const pillOf = (health, summary = {}, extra = {}) =>
  EnvironmentPillRules.of({ environment: "prod", reading: reading(health), summary: { environment: "prod", lastDeploy: null, running: null, weekly: [], ...summary }, ...extra });

test("a healthy pill shows the serving version without its v; a calm state names no word", () => {
  const pill = pillOf("healthy", { lastDeploy: deploy(), running: deploy() });
  assert.equal(pill.state, "healthy");
  assert.equal(pill.detail, "1.2.0");
  assert.equal(pill.release, "v1.2.0");
  assert.equal(shortRelease("0.5.0+abc123"), "0.5.0");
  assert.equal(shortRelease("sha-abc1234"), "sha-abc1234");
});

test("no target and nothing deployed are one dashed state that names nothing more", () => {
  for (const health of ["missing", "idle"]) {
    const pill = pillOf(health);
    assert.equal(pill.state, "none");
    assert.equal(pill.detail, null);
  }
  assert.equal(pillOf("missing").hasTarget, false);
  assert.equal(pillOf("idle").hasTarget, true);
});

test("a live rollout is deploying, a failure with no success since is failed, and a roll back is degraded", () => {
  const running = deploy({ id: "ok", submittedAt: "2026-10-01T10:00:00+00:00" });
  assert.equal(pillOf("healthy", { lastDeploy: deploy({ outcome: "pending", status: "running" }), running }).state, "deploying");
  assert.equal(pillOf("healthy", { lastDeploy: deploy({ outcome: "failed", status: "failed" }), running }).state, "failed");
  const rolledBack = pillOf("healthy", { lastDeploy: deploy({ outcome: "failed", status: "rolled_back", release: "v1.3.0" }), running });
  assert.equal(rolledBack.state, "degraded");
  assert.equal(rolledBack.detail, "degraded");
  // The previous release still serves, and the details name the cause with it.
  assert.equal(rolledBack.release, "v1.2.0");
  assert.equal(rolledBack.cause, "rolled back to v1.2.0");
  assert.equal(pillOf("healthy", { lastDeploy: deploy({ outcome: "failed", status: "rolled_back" }) }).cause, "rolled back to the previous release");
  // A success after the failure clears it.
  const later = deploy({ id: "later", submittedAt: "2026-10-07T10:00:00+00:00" });
  assert.equal(pillOf("healthy", { lastDeploy: deploy({ outcome: "failed", status: "failed" }), running: later }).state, "healthy");
});

test("vitals decide failing, degraded and unread; a host that cannot be read outranks a calm rollout", () => {
  assert.equal(pillOf("failing", { lastDeploy: deploy(), running: deploy() }).state, "failed");
  const strained = pillOf("warning", { lastDeploy: deploy(), running: deploy() });
  assert.deepEqual([strained.state, strained.cause], ["degraded", "restarts or host pressure"]);
  assert.equal(pillOf("unknown").detail, "no reading");
});

test("a product its own CI deploys shows the console's version in the environment it serves", () => {
  const served = pillOf("missing", {}, { ci: { version: "0.5.0+abc" } });
  assert.deepEqual([served.state, served.detail, served.release], ["ci", "0.5.0", "0.5.0+abc"]);
  assert.equal(pillOf("missing", {}, { ci: { version: null } }).detail, null);
});

test("a product's condition is the worst of its pills, and its facts carry the newest deploy and weekly series", () => {
  const pills = [pillOf("healthy", { lastDeploy: deploy(), running: deploy() }), pillOf("warning")];
  assert.equal(conditionOf(pills), "attention");
  assert.equal(conditionOf([pillOf("missing"), pillOf("idle")]), "idle");
  assert.equal(conditionOf([pillOf("unknown"), pillOf("missing")]), "unknown");
  const summary = {
    product: "foreverpin", workspace: "ventures", succeeded: 3, failed: 1,
    environments: [
      { environment: "dev", lastDeploy: deploy({ submittedAt: "2026-10-07T10:00:00Z" }), running: null, weekly: [1, 2] },
      { environment: "prod", lastDeploy: deploy({ submittedAt: "2026-10-05T10:00:00Z" }), running: null, weekly: [0, 1] },
    ],
  };
  const devPill = { ...pills[0], environment: "dev" };
  const facts = factsOf("foreverpin", [devPill, pills[1]], summary);
  assert.equal(facts.lastDeployedAt, "2026-10-07T10:00:00Z");
  assert.deepEqual(facts.series.map((item) => [item.environment, item.values]), [["dev", [1, 2]], ["prod", [0, 1]]]);
  assert.equal(factsOf("never", [pillOf("missing")], undefined).lastDeployedAt, null);
});

test("the sidebar's mark reads the latest rollouts alone", () => {
  const env = (lastDeploy, running) => ({ environment: "dev", lastDeploy, running, weekly: [] });
  const of = (...environments) => rolloutConditionOf({ product: "p", workspace: "w", succeeded: 0, failed: 0, environments });
  assert.equal(of(env(deploy({ outcome: "pending" }), deploy())), "deploying");
  assert.equal(of(env(deploy({ outcome: "failed", status: "failed", submittedAt: "2026-10-08T00:00:00Z" }), deploy())), "failing");
  assert.equal(of(env(deploy({ outcome: "failed", status: "rolled_back", submittedAt: "2026-10-08T00:00:00Z" }), deploy())), "attention");
  assert.equal(of(env(deploy(), deploy())), "healthy");
  assert.equal(of(), "idle");
  assert.equal(rolloutConditionOf(undefined), "idle");
});

test("search, workspaces, status and sort round-trip through the address, defaults left out", () => {
  assert.deepEqual(ProductFilterQuery.parse({}), DefaultProductFilter);
  assert.deepEqual(ProductFilterQuery.toQuery(DefaultProductFilter), {});
  const state = { search: "qr codes", workspaces: ["ventures", "platform"], status: "failing", sort: "last-deployed" };
  const query = ProductFilterQuery.toQuery(state);
  assert.deepEqual(query, { q: "qr codes", workspace: "ventures,platform", status: "failing", sort: "last-deployed" });
  assert.deepEqual(ProductFilterQuery.parse(query), state);
  assert.deepEqual(ProductFilterQuery.parse({ status: "bogus", sort: ["oldest-updated"], workspace: "a,,a" }),
    { search: "", workspaces: ["a"], status: "all", sort: "oldest-updated" });
  assert.equal(ProductFilterQuery.isNarrowed({ ...DefaultProductFilter, sort: "failed-first" }), false);
  assert.equal(ProductFilterQuery.isNarrowed({ ...DefaultProductFilter, search: "x" }), true);
});

const alpha = product({ slug: "alpha", name: "Alpha", description: "Styled QR codes" });
const beta = product({ slug: "beta", name: "Beta", description: "Short links" });
const gamma = product({ slug: "gamma", name: "Gamma", description: "Never deployed" });
const facts = {
  alpha: { condition: "healthy", lastDeployedAt: "2026-10-01T00:00:00Z" },
  beta: { condition: "failing", lastDeployedAt: "2026-10-07T00:00:00Z" },
  gamma: { condition: "idle", lastDeployedAt: null },
};
const factsFor = (slug) => facts[slug];
const apply = (fields) => ProductFilterRules.apply([gamma, beta, alpha], factsFor, { ...DefaultProductFilter, ...fields })
  .map((item) => item.slug);

test("search matches every word against name, slug or description", () => {
  assert.deepEqual(apply({ search: "qr styled" }), ["alpha"]);
  assert.deepEqual(apply({ search: "BETA" }), ["beta"]);
  assert.deepEqual(apply({ search: "nothing" }), []);
});

test("status keeps the products whose condition it names", () => {
  assert.deepEqual(apply({ status: "failing" }), ["beta"]);
  assert.deepEqual(apply({ status: "healthy" }), ["alpha"]);
  assert.deepEqual(apply({ status: "idle" }), ["gamma"]);
});

test("sorts order by name, by deploy time with never-deployed last, and by outcome", () => {
  assert.deepEqual(apply({}), ["alpha", "beta", "gamma"]);
  assert.deepEqual(apply({ sort: "last-deployed" }), ["beta", "alpha", "gamma"]);
  assert.deepEqual(apply({ sort: "oldest-updated" }), ["alpha", "beta", "gamma"]);
  assert.deepEqual(apply({ sort: "failed-first" }), ["beta", "alpha", "gamma"]);
  assert.deepEqual(apply({ sort: "succeeded-first" }), ["alpha", "beta", "gamma"]);
});

test("a workspace's figures count its shown products' conditions and its rollouts' success rate", () => {
  const stats = workspaceStatsOf(
    [{ condition: "failing" }, { condition: "attention" }, { condition: "unknown" }, { condition: "deploying" }, undefined],
    { workspace: "w", succeeded: 9, failed: 3, weekly: [{ succeeded: 9, failed: 3 }] },
  );
  assert.deepEqual([stats.products, stats.failing, stats.attention, stats.deploying, stats.successRate], [5, 1, 2, 1, 75]);
  assert.equal(workspaceStatsOf([], undefined).successRate, null);
});

test("sites group by name across environments, public first, private marked; one address opens directly", () => {
  const withSites = product({
    homepage: "https://foreverpin.app",
    environments: [
      { name: "prod", secrets: null, sites: [{ name: "web", url: "https://foreverpin.app", exposure: "public" },
        { name: "admin", url: "https://admin.foreverpin.app", exposure: "private" }] },
      { name: "dev", secrets: null, sites: [{ name: "web", url: "https://dev.foreverpin.app", exposure: "public" },
        { name: "evil", url: "javascript:alert(1)", exposure: "public" }] },
    ],
  });
  const links = siteLinksOf(withSites);
  assert.deepEqual(links.map((link) => [link.name, link.private, Object.keys(link.addresses)]),
    [["web", false, ["dev", "prod"]], ["admin", true, ["prod"]]]);
  assert.equal(directSiteOf(withSites, links), null);
  const one = product({ environments: [{ name: "prod", secrets: null, sites: [{ name: "web", url: "https://haven.uz", exposure: "public" }] }] });
  assert.equal(directSiteOf(one, siteLinksOf(one)).host, "haven.uz");
  const homepageOnly = product({ homepage: "https://example.com/" });
  assert.equal(directSiteOf(homepageOnly, []).host, "example.com");
  assert.equal(directSiteOf(product(), []), null);
});

test("Products returns to the product last opened from elsewhere, and leads up to the grid from inside Products", () => {
  const remembered = { slug: "foreverpin", tab: "releases" };
  assert.equal(productsDestination("/deployments", remembered), "/p/foreverpin/releases");
  assert.equal(productsDestination("/", { slug: "foreverpin", tab: null }), "/p/foreverpin");
  assert.equal(productsDestination("/p/foreverpin", remembered), "/products");
  assert.equal(productsDestination("/products", remembered), "/products");
  assert.equal(productsDestination("/servers", null), "/products");
});

test("a workspace's group is addressed by its hash", () => {
  assert.equal(ConsolePaths.workspace("own-ventures"), "/products#workspace-own-ventures");
  assert.equal(workspaceOfHash("#workspace-own-ventures"), "own-ventures");
  assert.equal(workspaceOfHash("#other"), null);
  assert.equal(workspaceOfHash(""), null);
});
