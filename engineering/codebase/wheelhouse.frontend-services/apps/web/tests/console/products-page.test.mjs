import assert from "node:assert/strict";
import { after, test } from "node:test";
import { stop } from "esbuild";
import { bundleModule } from "./BundleModule.mjs";
import { product } from "./LoadModule.mjs";

const {
  EnvironmentPillRules, shortRelease, factsOf, conditionOf, rolloutConditionOf, ProductFilterQuery, ProductFilterRules,
  DefaultProductFilter, workspaceStatsOf, siteLinksOf, directSiteOf, productsDestination, workspaceOfHash, ConsolePaths,
  RolloutBuckets, TrendWindows, productTrailOf,
} = await bundleModule(`
  export {
    EnvironmentPillRules, shortRelease, factsOf, conditionOf, rolloutConditionOf, ProductFilterQuery, ProductFilterRules,
    DefaultProductFilter, workspaceStatsOf, siteLinksOf, directSiteOf, RolloutBuckets, TrendWindows,
  } from './src/domain/portfolio';
  export { productsDestination, workspaceOfHash, ConsolePaths, productTrailOf } from './src/application/navigation';
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

test("a partial rollout reads mixed with how many services run the new release, and one corner marker at most", () => {
  const running = deploy({ id: "ok", release: "v0.8.4", submittedAt: "2026-10-01T10:00:00+00:00" });
  const partial = deploy({
    id: "p", release: "v0.9.0", status: "rollback_failed", outcome: "failed", state: "partial", substate: "partly_rolled_back",
    attention: "act", servicesOnRelease: 2, serviceCount: 3, watch: null, reason: "jobs did not become healthy",
  });
  const mixed = pillOf("healthy", { lastDeploy: partial, running });
  assert.deepEqual([mixed.state, mixed.detail, mixed.cause], ["mixed", "2/3 on 0.9.0", "jobs did not become healthy"]);
  assert.equal(pillOf("healthy", { lastDeploy: { ...partial, servicesOnRelease: null, serviceCount: null }, running }).detail, "mixed");

  // A newer attempt that changed nothing but needs a look marks the corner; a refusal never does.
  const served = deploy({ state: "succeeded", attention: "none", substate: "passed", watch: "passed" });
  const checkFailed = deploy({ id: "c", status: "failed", outcome: "failed", state: "not_applied", substate: "check_failed", attention: "look" });
  assert.equal(pillOf("healthy", { lastDeploy: served, running: served, lastAttempt: checkFailed }).marker, "attempt");
  assert.equal(pillOf("healthy", { lastDeploy: served, running: served, lastAttempt: null }).marker, null);
  // A release in its watch window wears the eye; a waiting deploy the clock, which outranks it.
  const watched = deploy({ state: "succeeded", attention: "none", substate: "watching", watch: "watching" });
  assert.equal(pillOf("healthy", { lastDeploy: watched, running: watched }).marker, "watching");
  assert.equal(pillOf("healthy", { lastDeploy: watched, running: watched }, { waiting: true }).marker, "waiting");
  const stillHealthy = pillOf("healthy", { lastDeploy: served, running: served, lastAttempt: checkFailed });
  assert.equal(stillHealthy.state, "healthy", "the pill shows the environment, not the attempt");
});

test("vitals decide failing, degraded and unread; a host that cannot be read outranks a calm rollout", () => {
  assert.equal(pillOf("failing", { lastDeploy: deploy(), running: deploy() }).state, "failed");
  const strained = pillOf("warning", { lastDeploy: deploy(), running: deploy() });
  assert.deepEqual([strained.state, strained.cause], ["degraded", "restarts or host pressure"]);
  assert.equal(pillOf("unknown").detail, "no reading");
});

test("a product's pills read CI from its stored deploy mode, and only the console's own product shows a version", () => {
  const console = { environment: "prod", version: "0.5.0+abc" };
  assert.deepEqual(EnvironmentPillRules.ciOf({ deploy: "ci", targets: 0, console }, "prod"), { version: "0.5.0+abc" });
  assert.deepEqual(EnvironmentPillRules.ciOf({ deploy: "ci", targets: 0, console }, "dev"), { version: null });
  assert.deepEqual(EnvironmentPillRules.ciOf({ deploy: "ci", targets: 0, console: null }, "prod"), { version: null },
    "another CI-deployed product reads CI without this console's version");
  assert.equal(EnvironmentPillRules.ciOf({ deploy: "wheelhouse", targets: 0, console }, "prod"), null,
    "the console's own slug decides nothing once its mode says Wheelhouse");
  assert.equal(EnvironmentPillRules.ciOf({ deploy: "none", targets: 0, console: null }, "prod"), null);
  assert.equal(EnvironmentPillRules.ciOf({ deploy: "ci", targets: 2, console: null }, "prod"), null, "its targets read health");
  assert.equal(EnvironmentPillRules.ciOf({ deploy: undefined, targets: 0, console }, "prod"), null);
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

test("search, workspaces, status, range and sort round-trip through the address, defaults left out", () => {
  assert.deepEqual(ProductFilterQuery.parse({}), DefaultProductFilter);
  assert.deepEqual(DefaultProductFilter.range, { kind: "preset", preset: "90d" });
  assert.deepEqual(ProductFilterQuery.toQuery(DefaultProductFilter), {});
  const state = { search: "qr codes", workspaces: ["ventures", "platform"], status: "failing",
    range: { kind: "preset", preset: "30d" }, sort: "last-deployed" };
  const query = ProductFilterQuery.toQuery(state);
  assert.deepEqual(query, { q: "qr codes", workspace: "ventures,platform", status: "failing", range: "30d", sort: "last-deployed" });
  assert.deepEqual(ProductFilterQuery.parse(query), state);
  assert.deepEqual(ProductFilterQuery.parse({ status: "bogus", sort: ["oldest-updated"], workspace: "a,,a", range: "1y" }),
    { search: "", workspaces: ["a"], status: "all", range: DefaultProductFilter.range, sort: "oldest-updated" });
  // A custom range is written as the Deployments page writes it, and wins over a preset.
  const custom = { ...DefaultProductFilter, range: { kind: "custom", from: "2026-09-01T00:00", to: "2026-10-01T00:00" } };
  assert.deepEqual(ProductFilterQuery.toQuery(custom), { from: "2026-09-01T00:00", to: "2026-10-01T00:00" });
  assert.deepEqual(ProductFilterQuery.parse({ range: "7d", from: "2026-09-01T00:00", to: "2026-10-01T00:00" }).range, custom.range);
  assert.deepEqual(ProductFilterQuery.parse({ from: "2026-10-01T00:00", to: "2026-09-01T00:00" }).range, DefaultProductFilter.range);
  // The range and the sort change figures and order, not which workspaces and products show.
  assert.equal(ProductFilterQuery.isNarrowed({ ...DefaultProductFilter, sort: "failed-first" }), false);
  assert.equal(ProductFilterQuery.isNarrowed(custom), false);
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

test("a workspace's figures count its shown products now and their rollouts over the range", () => {
  const stats = workspaceStatsOf(
    [{ condition: "failing" }, { condition: "attention" }, { condition: "unknown" }, { condition: "deploying" }, undefined],
    { from: "2026-10-13", to: "2026-10-19", days: [{ date: "2026-10-14", succeeded: 7, failed: 1 }, { date: "2026-10-19", succeeded: 2, failed: 2 }] },
  );
  // Degraded counts the degraded products only; an unread one is no reading, not degraded.
  assert.deepEqual([stats.products, stats.failing, stats.degraded, stats.deploying], [5, 1, 1, 1]);
  assert.deepEqual([stats.rollouts.succeeded, stats.rollouts.failed, stats.rollouts.successRate, stats.rollouts.size],
    [9, 3, 75, "day"]);
  assert.equal(stats.rollouts.buckets.length, 7);
  // Until the range is read there are no rollout figures; a range without rollouts has no rate.
  assert.equal(workspaceStatsOf([], undefined).rollouts, null);
  assert.equal(workspaceStatsOf([], { from: "2026-10-13", to: "2026-10-19", days: [] }).rollouts.successRate, null);
});

test("a range's rollouts fall into days up to a month, weeks up to six months, months beyond", () => {
  assert.equal(RolloutBuckets.sizeOf("2026-10-18", "2026-10-19"), "day");
  assert.equal(RolloutBuckets.sizeOf("2026-09-19", "2026-10-19"), "day");
  assert.equal(RolloutBuckets.sizeOf("2026-07-21", "2026-10-19"), "week");
  assert.equal(RolloutBuckets.sizeOf("2025-10-19", "2026-10-19"), "month");
  // Every column of the range is present, oldest first, empty or not.
  const days = RolloutBuckets.of({ from: "2026-10-18", to: "2026-10-19", days: [{ date: "2026-10-19", succeeded: 2, failed: 1 }] });
  assert.deepEqual(days.map((item) => [item.start, item.succeeded, item.failed]), [["2026-10-18", 0, 0], ["2026-10-19", 2, 1]]);
  // Weeks start on Monday; the first may begin before the range.
  const weeks = RolloutBuckets.of({ from: "2026-07-22", to: "2026-10-19", days: [
    { date: "2026-07-22", succeeded: 1, failed: 0 }, { date: "2026-07-26", succeeded: 1, failed: 1 },
    { date: "2026-10-19", succeeded: 3, failed: 0 }, { date: "2026-12-01", succeeded: 9, failed: 9 },
  ] });
  assert.equal(weeks[0].start, "2026-07-20");
  assert.deepEqual([weeks[0].succeeded, weeks[0].failed], [2, 1]);
  assert.equal(weeks.at(-1).start, "2026-10-19");
  assert.equal(weeks.at(-1).succeeded, 3);
  assert.equal(weeks.length, 14);
  const months = RolloutBuckets.of({ from: "2025-10-20", to: "2026-10-19", days: [{ date: "2026-02-14", succeeded: 4, failed: 0 }] });
  assert.equal(months.length, 13);
  assert.deepEqual([months[4].start, months[4].succeeded], ["2026-02-01", 4]);
  assert.deepEqual(RolloutBuckets.of({ from: "2026-10-19", to: "2026-10-18", days: [] }), []);
});

test("product trends cover every week the range touches, at most 52, and pin a past range's end", () => {
  const monday = new Date(2026, 9, 19, 9, 0);
  assert.deepEqual(TrendWindows.of({ kind: "preset", preset: "24h" }, monday), { weeks: 2, to: null });
  assert.deepEqual(TrendWindows.of({ kind: "preset", preset: "24h" }, new Date(2026, 9, 21, 9, 0)), { weeks: 1, to: null });
  assert.equal(TrendWindows.of({ kind: "preset", preset: "90d" }, monday).weeks, 14);
  assert.equal(TrendWindows.of({ kind: "preset", preset: "12m" }, monday).weeks, 52);
  const past = TrendWindows.of({ kind: "custom", from: "2026-09-07T00:00", to: "2026-09-21T00:00" }, monday);
  assert.equal(past.weeks, 2);
  assert.equal(past.to, new Date(2026, 8, 21).toISOString());
});

test("the sidebar's trail names the open product's workspace and product, or the slug until they arrive", () => {
  const platform = { id: "w-platform", slug: "platform", name: "Platform", description: "", order: 0 };
  const wheelhouse = product({ id: "p-wh", slug: "wheelhouse", name: "Wheelhouse", workspaceId: platform.id });
  const trail = productTrailOf("wheelhouse", [wheelhouse], [platform]);
  assert.equal(trail.workspace, platform);
  assert.deepEqual([trail.slug, trail.name], ["wheelhouse", "Wheelhouse"]);
  assert.deepEqual(productTrailOf("wheelhouse", [], []), { workspace: null, slug: "wheelhouse", name: "wheelhouse", product: null });
  assert.equal(productTrailOf("orphan", [product({ slug: "orphan", workspaceId: "w-gone" })], [platform]).workspace, null);
  assert.equal(productTrailOf(null, [wheelhouse], [platform]), null);
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
