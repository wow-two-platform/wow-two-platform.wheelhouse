import assert from "node:assert/strict";
import { after, test } from "node:test";
import { stop } from "esbuild";
import { bundleModule } from "./BundleModule.mjs";

// The real rules: the activity calendar, the entity picker's selection model, the filter's address round trip and the
// history wire decoding.
const history = await bundleModule(`
  export { ActivityCalendar, EntitySelection, EntityKeys, DeploymentFilters, StatusFilter, RangePreset, DeploymentIssues, DeploymentStates, DayLanes } from './src/domain/deployments';
  export { readDeploymentQuery, writeDeploymentQuery, mergeDeploymentQuery, buildEntityTree } from './src/application/deployments';
  export { releaseLocation, readReleaseSelection, isSelectedRelease } from './src/application/deployments/ReleaseSelection';
  export { DeploymentItemSchema, DeploymentDetailSchema, DeploymentIssueSchema, DeploymentActivitySchema } from './src/integration/deployments/historySchemas';
  export { scopeOf } from './src/application/navigation/Scope';
  export { HintPlacement } from './src/presentation/deployments/components/history/HintPlacement';
`);
after(() => stop());
const { ActivityCalendar, EntitySelection, EntityKeys, DeploymentFilters } = history;

test("a day's fill steps up at 1, 2–3, 4–6 and 7+ rollouts", () => {
  assert.deepEqual([0, 1, 2, 3, 4, 6, 7, 40].map(ActivityCalendar.level), [0, 1, 2, 2, 3, 3, 4, 4]);
});

const day = (calm, look, act, notApplied = 0) => ({ succeeded: calm + look, failed: act, calm, look, act, notApplied });

test("a day takes the loudest attention of its applied rollouts: green, amber with a half-dot, red with a cross", () => {
  assert.equal(ActivityCalendar.tone(undefined), "empty");
  assert.equal(ActivityCalendar.tone(day(0, 0, 0)), "empty");
  assert.equal(ActivityCalendar.tone(day(3, 0, 0)), "success");
  assert.equal(ActivityCalendar.tone(day(3, 1, 0)), "warning");
  assert.equal(ActivityCalendar.tone(day(3, 1, 1)), "danger");
  // A deploy that changed nothing never colours a day.
  assert.equal(ActivityCalendar.tone(day(0, 0, 0, 4)), "empty");
  assert.equal(ActivityCalendar.count(day(3, 1, 1, 4)), 5);
  // Shape marks, so colour is never the only signal.
  assert.deepEqual(["empty", "success", "danger", "warning"].map(ActivityCalendar.mark), ["none", "none", "cross", "half"]);
});

test("an answer from before the taxonomy colours a failure red and a success green", () => {
  assert.equal(ActivityCalendar.tone({ succeeded: 5, failed: 1 }), "danger");
  assert.equal(ActivityCalendar.tone({ succeeded: 5, failed: 0 }), "success");
  assert.equal(ActivityCalendar.count({ succeeded: 5, failed: 1 }), 6);
});

test("every day names its date and counts", () => {
  assert.match(ActivityCalendar.label("2026-10-14", day(3, 1, 1, 2)),
    /2026: 3 succeeded · 1 needs a look · 1 needs action · 2 changed nothing$/);
  assert.match(ActivityCalendar.label("2026-10-14", undefined), /no deployments$/);
});

test("the calendar lays weeks out as Monday-first columns with the range's edges left empty", () => {
  // 2026-10-01 is a Thursday; 2026-10-14 a Wednesday.
  const calendar = ActivityCalendar.build("2026-10-01", "2026-10-14", [
    { date: "2026-10-04", ...day(1, 1, 0) },
    { date: "2026-10-14", ...day(0, 0, 7) },
  ]);
  assert.equal(calendar.weeks.length, 3);
  assert.deepEqual(calendar.weeks[0].cells.map((cell) => cell?.date ?? null), [
    null, null, null, "2026-10-01", "2026-10-02", "2026-10-03", "2026-10-04",
  ]);
  assert.equal(calendar.weeks[0].start, "2026-09-28");
  const sunday = calendar.weeks[0].cells[6];
  assert.deepEqual([sunday.tone, sunday.level, sunday.mark], ["warning", 2, "half"]);
  const last = calendar.weeks[2].cells.filter(Boolean).at(-1);
  assert.deepEqual([last.date, last.tone, last.level, last.mark], ["2026-10-14", "danger", 4, "cross"]);
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
    workspace: "ocharo", service: ["foreverpin/web", "pose-coach/api"], status: "needs-action", range: "30d", view: "list", day: "2026-10-04",
  };
  const state = history.readDeploymentQuery(query);
  assert.deepEqual(state.filter.entities, ["w:ocharo", "s:foreverpin/web", "s:pose-coach/api"]);
  assert.equal(state.filter.status, "needs-action");
  // The old address's failed reads as needs action; look round-trips.
  assert.equal(history.readDeploymentQuery({ status: "failed" }).filter.status, "needs-action");
  assert.equal(history.readDeploymentQuery({ status: "look" }).filter.status, "look");
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
  const filter = { entities: ["w:ocharo", "p:foreverpin", "s:pose-coach/api"], status: "needs-action", range: { kind: "preset", preset: "24h" } };
  assert.deepEqual(DeploymentFilters.params(filter, now), [
    ["workspace", "ocharo"], ["product", "foreverpin"], ["service", "pose-coach/api"], ["status", "needs-action"],
    ["from", "2026-10-16T12:00:00.000Z"], ["to", "2026-10-17T12:00:00.000Z"],
  ]);
  // The viewer's Show unchanged toggle asks for the deploys that changed nothing, and keys its own cache entry.
  assert.ok(DeploymentFilters.params(filter, now, { unchanged: true }).some(([key, value]) => key === "notApplied" && value === "true"));
  assert.notEqual(DeploymentFilters.key(filter, true), DeploymentFilters.key(filter));
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

test("an older activity answer without busiest products still decodes", () => {
  const activity = history.DeploymentActivitySchema.parse({ from: "2026-04-18", to: "2026-10-18", timeZone: "UTC", days: [] });
  assert.deepEqual(activity.products, []);
});

test("a deployment reads as the API's state, coloured by attention; dashed when it changed nothing", () => {
  const { DeploymentStates } = history;
  const partial = DeploymentStates.of({
    status: "rollback_failed", state: "partial", substate: "partly_rolled_back", attention: "act",
    cause: { kind: "service", origin: "jobs", message: "jobs did not become healthy", at: null },
  });
  assert.deepEqual([partial.word, partial.detail, partial.tone, partial.outline], ["Partial", "partly rolled back", "danger", "solid"]);
  assert.equal(DeploymentStates.label(partial), "Partial, partly rolled back: jobs did not become healthy");
  const checkFailed = DeploymentStates.of({ status: "failed", state: "not_applied", substate: "check_failed", attention: "look" });
  assert.deepEqual([checkFailed.word, checkFailed.tone, checkFailed.outline], ["Not applied", "warning", "dashed"]);
  const superseded = DeploymentStates.of({ status: "rejected", state: "not_applied", substate: "superseded", attention: "none" });
  assert.equal(superseded.tone, "calm", "a superseded deploy needs nothing, so it stays neutral");
  assert.equal(DeploymentStates.of({ status: "unknown", state: "unknown", attention: "muted" }).outline, "dotted");
});

test("a read without a state takes its runner status the old way", () => {
  const { DeploymentStates } = history;
  const legacy = (status, watch) => {
    const reading = DeploymentStates.of({ status, watch });
    return `${reading.state}/${reading.attention}`;
  };
  assert.equal(legacy("succeeded"), "succeeded/none");
  assert.equal(legacy("succeeded", { status: "degraded", findings: [] }), "degraded/look");
  assert.equal(legacy("rolled_back"), "rolled_back/look");
  assert.equal(legacy("rollback_failed"), "failed/act");
  assert.equal(legacy("interrupted"), "failed/act");
  assert.equal(legacy("rejected"), "not_applied/none");
  for (const status of ["submitting", "queued", "running"]) assert.equal(legacy(status), "in_progress/info");
  assert.equal(legacy("unknown"), "unknown/muted");
});

test("a chosen day counts only the states that occurred, calm to urgent, then the rest, each at its loudest", () => {
  const { DeploymentStates } = history;
  const counts = DeploymentStates.counts([
    { status: "running" }, { status: "rejected" }, { status: "succeeded" }, { status: "rolled_back" }, { status: "failed" },
    { status: "succeeded" }, { status: "failed", state: "not_applied", substate: "check_failed", attention: "look" },
  ]);
  assert.deepEqual(counts.map((count) => `${count.count} ${count.word} ${count.attention}`), [
    "2 Succeeded none", "1 Rolled back look", "1 Failed act", "1 In progress info", "2 Not applied look",
  ]);
  assert.deepEqual(DeploymentStates.counts([]), []);
});

test("a deployment's services say how each ended, in words on a phone, and list its impact origin first", () => {
  const { DeploymentStates } = history;
  const service = (name, result, origin = false) => ({ service: name, result, origin, from: null, to: null, health: null });
  const partial = [service("desk", "restored"), service("jobs", "failed", true), service("image-worker", "affected")];
  assert.equal(DeploymentStates.stripSummary(partial), "1 of 3 failed");
  assert.equal(DeploymentStates.stripSummary([service("api", "updated"), service("web", "unchanged")]), "1 of 2 updated");
  assert.equal(DeploymentStates.stripSummary(null), null);
  const impact = DeploymentStates.impact(partial, [{ service: "image-worker", finding: "restarting", via: "jobs", at: null }]);
  assert.deepEqual(impact.map((line) => `${line.role}:${line.service}`), ["origin:jobs", "effect:image-worker", "other:desk"]);
  assert.equal(impact[1].text, "restarting, likely via jobs");
  assert.deepEqual(DeploymentStates.onRelease(partial), { on: 0, total: 3 });
});

test("the steps split where changes start, and each state offers the action that fits it", () => {
  const { DeploymentStates } = history;
  const steps = ["Check target", "Snapshot data", "Pull images", "Start desk", "Start jobs", "Roll back to v0.8.4", "Publish sites"]
    .map((name) => ({ name, status: "succeeded" }));
  const sections = DeploymentStates.sections(steps);
  assert.deepEqual(
    [sections.before.length, sections.changes.map((step) => step.name), sections.after.map((step) => step.name)],
    [3, ["Start desk", "Start jobs", "Roll back to v0.8.4"], ["Publish sites"]],
  );
  assert.deepEqual(DeploymentStates.sections(steps.slice(0, 2)).changes, [], "a rollout that stopped before changes has none");
  assert.deepEqual(["partial", "failed", "rolled_back", "not_applied", "succeeded"].map(DeploymentStates.action),
    ["reconcile", "reconcile", "redeploy", "retry", null]);
  assert.equal(DeploymentStates.nowRuns({ state: "partial" }, "prod", "v0.9.0", "v0.8.4"), "prod now runs two releases");
  assert.equal(DeploymentStates.nowRuns({ state: "rolled_back" }, "prod", "v0.9.0", "v0.8.4"), "prod runs v0.8.4 again");
});

test("history items decode the state, and an older item without one reads its runner status", () => {
  const item = history.DeploymentItemSchema.parse({
    id: "x", status: "rollback_failed", outcome: "failed", submittedAt: "2026-10-01T10:00:00Z", state: "partial",
    substate: "partly_rolled_back", attention: "act",
    services: [{ service: "jobs", result: "failed", origin: true }, { service: "desk", result: "teleported" }],
    watch: null,
  });
  assert.deepEqual([item.state, item.substate, item.attention], ["partial", "partly_rolled_back", "act"]);
  assert.deepEqual(item.services.map((service) => `${service.service}:${service.result}:${service.origin}`), ["jobs:failed:true", "desk:unknown:false"]);
  const old = history.DeploymentItemSchema.parse({
    id: "y", status: "succeeded", outcome: "succeeded", submittedAt: "2026-10-01T10:00:00Z",
    watch: { status: "superseded", findings: [] },
  });
  assert.deepEqual([old.state, old.substate, old.attention, old.watch.status], ["succeeded", "replaced", "none", "replaced"]);
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

// The chosen day as one compact row per product: lanes per environment, glyphs oldest first, rows by attention.
const deploy = (id, product, environment, at, state, attention, extra = {}) => ({
  id, product, environment, targetId: `${product}-${environment}`, release: `v1.0.${id}`, bundleId: null, actor: "sulton-max",
  status: "succeeded", state, substate: null, attention, cause: null, submittedAt: at, startedAt: at, ...extra,
});

test("a day row holds a lane per environment, each deploy oldest first, so the last is what runs now", () => {
  const { DayLanes } = history;
  const items = [
    deploy(3, "vault", "dev", "2026-10-19T10:00:00Z", "succeeded", "none"),
    deploy(1, "vault", "dev", "2026-10-19T08:00:00Z", "failed", "act"),
    deploy(2, "vault", "prod", "2026-10-19T09:00:00Z", "succeeded", "none"),
  ];
  const [row] = DayLanes.rows(items, new Map([["vault", "Secrets Vault"]]));
  assert.equal(row.name, "Secrets Vault");
  assert.deepEqual(row.lanes.map((lane) => lane.environment), ["dev", "prod"]);
  assert.deepEqual(row.lanes[0].glyphs.map((glyph) => glyph.release), ["v1.0.1", "v1.0.3"]);
  // Fixed the same day: the failed glyph stays, and the lane's latest deploy needs nothing.
  assert.equal(row.lanes[0].attention, null);
  assert.equal(row.attention, null);
  // A product with no deploy in an environment still has its lane, empty.
  const [only] = DayLanes.rows([deploy(4, "pin", "prod", "2026-10-19T09:00:00Z", "succeeded", "none")], new Map());
  assert.deepEqual(only.lanes.map((lane) => lane.glyphs.length), [0, 1]);
  assert.deepEqual(DayLanes.environments([deploy(5, "pin", "test", "2026-10-19T09:00:00Z", "succeeded", "none")]), ["dev", "prod", "test"]);
});

test("a row's bar follows its lanes' latest deploys, and a deploy that changed nothing never hides a failure", () => {
  const { DayLanes } = history;
  const lane = (...states) =>
    DayLanes.rows(states.map(([state, attention], index) =>
      deploy(index, "p", "dev", `2026-10-19T0${index}:00:00Z`, state, attention)), new Map())[0].lanes[0].attention;
  assert.equal(lane(["succeeded", "none"], ["failed", "act"]), "act");
  assert.equal(lane(["failed", "act"], ["not_applied", "none"]), "act");
  assert.equal(lane(["succeeded", "none"], ["not_applied", "look"]), "look");
  assert.equal(lane(["rolled_back", "look"], ["succeeded", "none"]), null);
  assert.equal(lane(["degraded", "look"]), "look");
  assert.equal(lane(["failed", "act"], ["in_progress", "info"]), null);
});

test("rows needing action lead, then those worth a look, then the rest, by name within each", () => {
  const { DayLanes } = history;
  const items = [
    deploy(1, "zeta", "dev", "2026-10-19T08:00:00Z", "succeeded", "none"),
    deploy(2, "alpha", "dev", "2026-10-19T08:00:00Z", "succeeded", "none"),
    deploy(3, "mid", "prod", "2026-10-19T08:00:00Z", "rolled_back", "look"),
    deploy(4, "last", "prod", "2026-10-19T08:00:00Z", "partial", "act"),
    deploy(5, null, "prod", "2026-10-19T08:00:00Z", "failed", "act", { targetId: "gone-prod" }),
  ];
  const rows = DayLanes.rows(items, new Map([["last", "Last"], ["mid", "Mid"], ["alpha", "Alpha"], ["zeta", "Zeta"]]));
  assert.deepEqual(rows.map((row) => [row.name, row.attention]), [
    ["gone-prod", "act"], ["Last", "act"], ["Mid", "look"], ["Alpha", null], ["Zeta", null],
  ]);
  assert.equal(rows[0].isProduct, false);
});

test("a glyph's tooltip says version, time, state and actor, and names the server of a several-server lane", () => {
  const { DayLanes } = history;
  const at = new Date(2026, 9, 19, 14, 5).toISOString();
  const items = [
    deploy(1, "nasiya", "prod", at, "failed", "act", { substate: "rollback_not_allowed", targetId: "nasiya-prod" }),
    deploy(2, "nasiya", "dev", at, "succeeded", "none", { targetId: "nasiya-dev" }),
  ];
  const targets = [
    { id: "nasiya-prod", product: "nasiya", environment: "prod", serverId: "ovh1" },
    { id: "nasiya-prod-hel1", product: "nasiya", environment: "prod", serverId: "hel1" },
    { id: "nasiya-dev", product: "nasiya", environment: "dev", serverId: "hel1" },
  ];
  const [row] = DayLanes.rows(items, new Map(), targets);
  assert.equal(DayLanes.hint(row.lanes[1].glyphs[0]), "v1.0.1 · 14:05 · Failed, rollback not allowed · sulton-max · on ovh1");
  assert.equal(DayLanes.hint(row.lanes[0].glyphs[0]), "v1.0.2 · 14:05 · Succeeded · sulton-max");
});

test("a glyph opens its release on the product's Deployments tab, as a list over the view's range", () => {
  const item = { product: "foreverpin", release: "v1.8.2", bundleId: "b-1", environment: "prod" };
  assert.deepEqual(history.releaseLocation(item, { kind: "preset", preset: "6m" }), {
    path: "/p/foreverpin/deployments", query: { view: "list", release: "v1.8.2", env: "prod" },
  });
  assert.deepEqual(history.releaseLocation(item, { kind: "custom", from: "2026-01-01T00:00", to: "2026-02-01T00:00" }).query, {
    from: "2026-01-01T00:00", to: "2026-02-01T00:00", view: "list", release: "v1.8.2", env: "prod",
  });
  assert.equal(history.releaseLocation({ ...item, release: null }, { kind: "preset", preset: "12m" }).query.release, "b-1");
  assert.equal(history.releaseLocation({ ...item, product: null }, { kind: "preset", preset: "6m" }), null);

  const selection = history.readReleaseSelection({ release: "v1.8.2", env: "prod", view: "list" });
  assert.deepEqual(selection, { release: "v1.8.2", environment: "prod" });
  assert.ok(history.isSelectedRelease(item, selection));
  assert.ok(!history.isSelectedRelease({ ...item, environment: "dev" }, selection));
  assert.ok(history.isSelectedRelease({ ...item, environment: "dev" }, history.readReleaseSelection({ release: "v1.8.2" })));
  assert.equal(history.readReleaseSelection({ env: "prod" }), null);
  // The selection's keys survive the view's own address writes.
  const state = history.readDeploymentQuery({ view: "list", release: "v1.8.2", env: "prod" });
  assert.deepEqual(history.mergeDeploymentQuery({ release: "v1.8.2", env: "prod" }, state), { release: "v1.8.2", env: "prod", view: "list" });
});
