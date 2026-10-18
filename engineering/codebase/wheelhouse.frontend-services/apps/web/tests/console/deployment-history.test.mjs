import assert from "node:assert/strict";
import { after, test } from "node:test";
import { stop } from "esbuild";
import { bundleModule } from "./BundleModule.mjs";

// The real rules: the activity calendar, the entity picker's selection model, the filter's address round trip and the
// history wire decoding.
const history = await bundleModule(`
  export { ActivityCalendar, EntitySelection, EntityKeys, DeploymentFilters, StatusFilter, RangePreset, DeploymentIssues } from './src/domain/deployments';
  export { readDeploymentQuery, writeDeploymentQuery, mergeDeploymentQuery, buildEntityTree } from './src/application/deployments';
  export { DeploymentItemSchema, DeploymentDetailSchema, DeploymentIssueSchema, DeploymentActivitySchema } from './src/integration/deployments/historySchemas';
  export { scopeOf } from './src/application/navigation/Scope';
  export { HintPlacement } from './src/presentation/deployments/components/history/HintPlacement';
`);
after(() => stop());
const { ActivityCalendar, EntitySelection, EntityKeys, DeploymentFilters } = history;

test("a day's fill steps up at 1, 2–3, 4–6 and 7+ rollouts", () => {
  assert.deepEqual([0, 1, 2, 3, 4, 6, 7, 40].map(ActivityCalendar.level), [0, 1, 2, 2, 3, 3, 4, 4]);
});

test("a day is green when all succeeded, red when all failed and amber when both", () => {
  assert.equal(ActivityCalendar.tone(undefined), "empty");
  assert.equal(ActivityCalendar.tone({ succeeded: 0, failed: 0 }), "empty");
  assert.equal(ActivityCalendar.tone({ succeeded: 3, failed: 0 }), "success");
  assert.equal(ActivityCalendar.tone({ succeeded: 0, failed: 2 }), "danger");
  assert.equal(ActivityCalendar.tone({ succeeded: 3, failed: 1 }), "warning");
  // Failed and mixed days carry a mark, so colour is never the only signal.
  assert.deepEqual(["empty", "success", "danger", "warning"].map(ActivityCalendar.mark), ["none", "none", "failed", "mixed"]);
});

test("with the Failed filter on, only failures colour a day, on the red scale", () => {
  const mixed = { succeeded: 5, failed: 1 };
  assert.equal(ActivityCalendar.tone(mixed, "failed"), "danger");
  assert.equal(ActivityCalendar.count(mixed, "failed"), 1);
  assert.equal(ActivityCalendar.tone({ succeeded: 5, failed: 0 }, "failed"), "empty");
  assert.equal(ActivityCalendar.tone(mixed, "succeeded"), "success");
  assert.equal(ActivityCalendar.count(mixed), 6);
});

test("every day names its date and counts", () => {
  assert.match(ActivityCalendar.label("2026-10-14", { succeeded: 3, failed: 1 }), /2026: 3 succeeded · 1 failed$/);
  assert.match(ActivityCalendar.label("2026-10-14", undefined), /no deployments$/);
  assert.match(ActivityCalendar.label("2026-10-14", { succeeded: 3, failed: 1 }, "failed"), /: 1 failed$/);
});

test("the calendar lays weeks out as Monday-first columns with the range's edges left empty", () => {
  // 2026-10-01 is a Thursday; 2026-10-14 a Wednesday.
  const calendar = ActivityCalendar.build("2026-10-01", "2026-10-14", [
    { date: "2026-10-04", succeeded: 1, failed: 1 },
    { date: "2026-10-14", succeeded: 0, failed: 7 },
  ]);
  assert.equal(calendar.weeks.length, 3);
  assert.deepEqual(calendar.weeks[0].cells.map((cell) => cell?.date ?? null), [
    null, null, null, "2026-10-01", "2026-10-02", "2026-10-03", "2026-10-04",
  ]);
  assert.equal(calendar.weeks[0].start, "2026-09-28");
  const sunday = calendar.weeks[0].cells[6];
  assert.deepEqual([sunday.tone, sunday.level, sunday.mark], ["warning", 2, "mixed"]);
  const last = calendar.weeks[2].cells.filter(Boolean).at(-1);
  assert.deepEqual([last.date, last.tone, last.level, last.mark], ["2026-10-14", "danger", 4, "failed"]);
  assert.equal(calendar.weeks[2].cells[3], null);
  assert.equal(calendar.weekdays.length, 7);
  assert.equal(calendar.months[0].column, 0);
});

test("six months of calendar label each month once, spaced so labels never overlap", () => {
  const calendar = ActivityCalendar.build("2026-04-17", "2026-10-17", []);
  assert.ok(calendar.weeks.length >= 26 && calendar.weeks.length <= 28);
  const columns = calendar.months.map((month) => month.column);
  assert.deepEqual(columns, [...columns].sort((a, b) => a - b));
  for (let index = 1; index < columns.length; index++) assert.ok(columns[index] - columns[index - 1] >= 3);
  assert.ok(calendar.months.length >= 6);
});

test("arrow keys move a week sideways and a day up or down", () => {
  assert.equal(ActivityCalendar.step("2026-10-14", "ArrowRight"), "2026-10-21");
  assert.equal(ActivityCalendar.step("2026-10-14", "ArrowLeft"), "2026-10-07");
  assert.equal(ActivityCalendar.step("2026-10-14", "ArrowDown"), "2026-10-15");
  assert.equal(ActivityCalendar.step("2026-11-01", "ArrowUp"), "2026-10-31");
  assert.equal(ActivityCalendar.step("2026-10-14", "Enter"), null);
});

const tree = [
  { slug: "ventures", name: "Ventures", products: [
    { slug: "foreverpin", name: "ForeverPin", services: ["api", "web"] },
    { slug: "pose-coach", name: "Pose Coach", services: ["api"] },
  ] },
  { slug: "ocharo", name: "Ocharo", products: [{ slug: "ocharo-shop", name: "Ocharo shop", services: ["api"] }] },
];
const node = {
  workspace: (workspace) => ({ kind: "workspace", workspace }),
  product: (workspace, product) => ({ kind: "product", workspace, product }),
  service: (workspace, product, service) => ({ kind: "service", workspace, product, service }),
};

test("the picker mixes a single service with a whole workspace", () => {
  let selection = EntitySelection.toggle(tree, [], node.service("ventures", "foreverpin", "web"));
  selection = EntitySelection.toggle(tree, selection, node.workspace("ocharo"));
  assert.deepEqual(selection, ["s:foreverpin/web", "w:ocharo"]);
  assert.equal(EntitySelection.state(tree, selection, node.workspace("ventures")), "partial");
  assert.equal(EntitySelection.state(tree, selection, node.product("ventures", "foreverpin")), "partial");
  assert.equal(EntitySelection.state(tree, selection, node.product("ocharo", "ocharo-shop")), "implied");
  assert.equal(EntitySelection.state(tree, selection, node.service("ocharo", "ocharo-shop", "api")), "implied");
  assert.equal(EntitySelection.state(tree, selection, node.service("ventures", "foreverpin", "api")), "unchecked");
  assert.deepEqual(EntitySelection.chips(tree, selection).map((chip) => chip.label), ["ForeverPin · web", "Ocharo"]);
});

test("picking a node drops what it already covers", () => {
  const services = ["s:foreverpin/api", "s:foreverpin/web", "p:ocharo-shop"];
  assert.deepEqual(EntitySelection.toggle(tree, services, node.product("ventures", "foreverpin")), ["p:ocharo-shop", "p:foreverpin"]);
  assert.deepEqual(EntitySelection.toggle(tree, ["p:foreverpin", "s:pose-coach/api"], node.workspace("ventures")), ["w:ventures"]);
  assert.deepEqual(EntitySelection.toggle(tree, ["w:ventures"], node.workspace("ventures")), []);
});

test("clearing a node a picked ancestor covers keeps its siblings picked", () => {
  assert.deepEqual(EntitySelection.toggle(tree, ["w:ventures"], node.product("ventures", "foreverpin")), ["p:pose-coach"]);
  assert.deepEqual(EntitySelection.toggle(tree, ["p:foreverpin"], node.service("ventures", "foreverpin", "web")), ["s:foreverpin/api"]);
  assert.deepEqual(EntitySelection.toggle(tree, ["w:ventures"], node.service("ventures", "foreverpin", "api")), [
    "p:pose-coach",
    "s:foreverpin/web",
  ]);
});

test("entity keys refuse anything but slugs and one product/service pair", () => {
  assert.deepEqual(EntityKeys.parse("s:foreverpin/web"), { kind: "service", product: "foreverpin", service: "web" });
  for (const key of ["s:foreverpin", "s:foreverpin/web/x", "p:Fore", "w:../x", "x:foreverpin", "p:"])
    assert.equal(EntityKeys.parse(key), null, key);
  assert.deepEqual(EntitySelection.normalize(["p:a", "p:a", "p:B", "w:ventures"]), ["p:a", "w:ventures"]);
});

test("the address round-trips entities, status, a preset or custom range, the view and the day", () => {
  const query = {
    workspace: "ocharo", service: ["foreverpin/web", "pose-coach/api"], status: "failed", range: "30d", view: "list", day: "2026-10-04",
  };
  const state = history.readDeploymentQuery(query);
  assert.deepEqual(state.filter.entities, ["w:ocharo", "s:foreverpin/web", "s:pose-coach/api"]);
  assert.equal(state.filter.status, "failed");
  assert.deepEqual(state.filter.range, { kind: "preset", preset: "30d" });
  assert.deepEqual([state.view, state.day], ["list", "2026-10-04"]);
  assert.deepEqual(history.writeDeploymentQuery(state), query);

  const custom = history.readDeploymentQuery({ from: "2026-10-01T09:00", to: "2026-10-08T18:30", product: "foreverpin" });
  assert.deepEqual(custom.filter.range, { kind: "custom", from: "2026-10-01T09:00", to: "2026-10-08T18:30" });
  assert.deepEqual(history.writeDeploymentQuery(custom), { product: "foreverpin", from: "2026-10-01T09:00", to: "2026-10-08T18:30" });
});

test("a plain address is the default view, and malformed values fall back rather than fail", () => {
  const plain = history.readDeploymentQuery({});
  assert.deepEqual(plain.filter, { entities: [], status: "all", range: { kind: "preset", preset: "6m" } });
  assert.deepEqual([plain.view, plain.day], ["grid", null]);
  assert.deepEqual(history.writeDeploymentQuery(plain), {});

  const broken = history.readDeploymentQuery({
    status: "maybe", range: "forever", from: "2026-10-08T00:00", to: "2026-10-01T00:00", view: "table", day: "yesterday",
    product: ["Bad", "foreverpin"], service: "foreverpin",
  });
  assert.deepEqual(broken.filter, { entities: ["p:foreverpin"], status: "all", range: { kind: "preset", preset: "6m" } });
  assert.deepEqual([broken.view, broken.day], ["grid", null]);
});

test("merging the view into an address keeps keys the view does not own", () => {
  const state = history.readDeploymentQuery({ status: "succeeded" });
  assert.deepEqual(history.mergeDeploymentQuery({ env: "prod", status: "failed", day: "2026-10-01" }, state), {
    env: "prod", status: "succeeded",
  });
});

test("the API parameters repeat entities, leave out all and resolve the range at now", () => {
  const now = new Date("2026-10-17T12:00:00Z");
  const filter = { entities: ["w:ocharo", "p:foreverpin", "s:pose-coach/api"], status: "failed", range: { kind: "preset", preset: "24h" } };
  assert.deepEqual(DeploymentFilters.params(filter, now), [
    ["workspace", "ocharo"], ["product", "foreverpin"], ["service", "pose-coach/api"], ["status", "failed"],
    ["from", "2026-10-16T12:00:00.000Z"], ["to", "2026-10-17T12:00:00.000Z"],
  ]);
  const withoutStatus = DeploymentFilters.params({ ...filter, status: "all" }, now, { withStatus: false });
  assert.ok(!withoutStatus.some(([key]) => key === "status"));
  const halfYear = DeploymentFilters.resolve({ kind: "preset", preset: "6m" }, now);
  assert.equal(halfYear.from.getMonth(), (now.getMonth() + 6) % 12);
  assert.equal(DeploymentFilters.key(filter), DeploymentFilters.key({ ...filter, entities: [...filter.entities].reverse() }));
  assert.ok(DeploymentFilters.isValidCustom("2026-10-01T09:00", "2026-10-01T09:01"));
  assert.ok(!DeploymentFilters.isValidCustom("2026-10-01T09:00", "2026-10-01T09:00"));
});

test("the entity tree lists each workspace's shown products with their services, inactive ones hidden", () => {
  const product = (slug, workspaceId, services, inactiveServices = []) => ({
    slug, name: slug.toUpperCase(), workspaceId, inactiveServices,
    identity: { services: services.map((service) => ({ service })) },
    release: { images: [{ service: "api", image: "x" }] },
  });
  const groups = [
    { workspace: { slug: "ventures", name: "Ventures" }, products: [product("foreverpin", "w", ["web", "worker"], ["worker"])] },
    { workspace: null, products: [product("orphan", null, [])] },
  ];
  assert.deepEqual(history.buildEntityTree(groups, false), [
    { slug: "ventures", name: "Ventures", products: [{ slug: "foreverpin", name: "FOREVERPIN", services: ["api", "web"] }] },
  ]);
  assert.deepEqual(history.buildEntityTree(groups, true)[0].products[0].services, ["api", "web", "worker"]);
});

test("history items decode with defaults, and an unknown status or outcome stays readable", () => {
  const item = history.DeploymentItemSchema.parse({ id: "x", status: "promoted", outcome: "later", submittedAt: "2026-10-01T10:00:00Z" });
  assert.equal(item.status, "unknown");
  assert.equal(item.outcome, "unknown");
  assert.deepEqual([item.changedServices, item.warnings, item.currentStep], [null, [], null]);
  const detail = history.DeploymentDetailSchema.safeParse({
    deployment: { id: "x", status: "succeeded", outcome: "succeeded", submittedAt: "2026-10-01T10:00:00Z" },
    sites: [{ name: "app", url: "javascript:alert(1)" }],
  });
  assert.equal(detail.success, false, "a site link must be http(s)");
});

test("one deployment's scope leads back to the Deployments page", () => {
  const trail = history.scopeOf({
    path: "/deployments/00000000-0000-4000-8000-000000000001", params: {}, query: {}, title: "Deployment 00000000",
    workspaces: [], products: [],
  });
  assert.deepEqual(trail.map((crumb) => [crumb.label, crumb.location.path]), [
    ["Home", "/"], ["Deployments", "/deployments"], ["Deployment 00000000", "/deployments/00000000-0000-4000-8000-000000000001"],
  ]);
});

test("an issue says what happened and offers Redeploy, or Reconcile when the target is likely locked", () => {
  const { DeploymentIssues, DeploymentIssueSchema } = history;
  const deployment = { id: "x", targetId: "fp-prod", bundleId: "fp-gh-2", status: "running", outcome: "pending",
    submittedAt: "2026-10-01T10:00:00Z", currentStep: { name: "Verify services", index: 4 } };
  const stuck = DeploymentIssueSchema.parse({ kind: "stuck", needsReconcile: true, at: "2026-10-01T10:00:05Z", deployment });
  assert.equal(DeploymentIssues.headline(stuck), "Stuck on Verify services");
  assert.deepEqual([DeploymentIssues.canRedeploy(stuck), DeploymentIssues.canReconcile(stuck)], [false, true]);
  const rolledBack = DeploymentIssueSchema.parse({ kind: "rolled_back", at: "2026-10-01T10:03:00Z",
    deployment: { ...deployment, status: "rolled_back", outcome: "failed", currentStep: null } });
  assert.equal(rolledBack.needsReconcile, false);
  assert.equal(DeploymentIssues.headline(rolledBack), "Rolled back to the previous release");
  assert.deepEqual([DeploymentIssues.canRedeploy(rolledBack), DeploymentIssues.canReconcile(rolledBack)], [true, false]);
  // A kind this console does not know reads as a failure; a bundle the runner no longer holds cannot be redeployed.
  const unknown = DeploymentIssueSchema.parse({ kind: "melted", at: "2026-10-01T10:03:00Z", deployment: { ...deployment, bundleId: null } });
  assert.equal(DeploymentIssues.headline(unknown), "Failed");
  assert.equal(DeploymentIssues.canRedeploy(unknown), false);
});

test("the range totals its days, and an older answer without busiest products still decodes", () => {
  assert.deepEqual(ActivityCalendar.totals([{ succeeded: 3, failed: 1 }, { succeeded: 0, failed: 2 }]), { succeeded: 3, failed: 3 });
  assert.deepEqual(ActivityCalendar.totals([]), { succeeded: 0, failed: 0 });
  const activity = history.DeploymentActivitySchema.parse({ from: "2026-04-18", to: "2026-10-18", timeZone: "UTC", days: [] });
  assert.deepEqual(activity.products, []);
});

test("the line under the calendar counts deployments, names running ones only while any run, and minds plurals", () => {
  assert.equal(ActivityCalendar.summary({ succeeded: 1, failed: 0 }), "1 deployment · 1 succeeded · 0 failed");
  assert.equal(ActivityCalendar.summary({ succeeded: 2, failed: 1 }, "all", 1), "4 deployments · 2 succeeded · 1 failed · 1 running");
  assert.equal(ActivityCalendar.summary({ succeeded: 0, failed: 0 }), "0 deployments · 0 succeeded · 0 failed");
  assert.equal(ActivityCalendar.summary({ succeeded: 0, failed: 1 }, "failed", 2), "1 deployment failed");
  assert.equal(ActivityCalendar.summary({ succeeded: 5, failed: 0 }, "succeeded"), "5 deployments succeeded");
});

test("the day tooltip sits above its cell, flips below at the top edge and slides inside the screen's sides", () => {
  const { HintPlacement } = history;
  const viewport = { width: 390, height: 844 };
  const size = { width: 200, height: 24 };
  // Room above: centred over the cell.
  assert.deepEqual(HintPlacement.place({ left: 190, top: 300, width: 20, height: 20 }, size, viewport), { left: 100, top: 270, side: "top" });
  // At the right edge: slid left to stay 8px inside, never cut off.
  assert.deepEqual(HintPlacement.place({ left: 360, top: 300, width: 20, height: 20 }, size, viewport), { left: 182, top: 270, side: "top" });
  // At the left edge, and too near the top: slid right and flipped below.
  assert.deepEqual(HintPlacement.place({ left: 0, top: 10, width: 20, height: 20 }, size, viewport), { left: 8, top: 36, side: "bottom" });
});
