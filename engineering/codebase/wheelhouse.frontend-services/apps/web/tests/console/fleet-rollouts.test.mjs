import assert from "node:assert/strict";
import { after, test } from "node:test";
import { stop } from "esbuild";
import { bundleModule } from "./BundleModule.mjs";

// The real rules of fleet rollouts and their wire decoding.
const fleets = await bundleModule(`
  export { FleetRollouts, FleetMaxItems } from './src/domain/deployments';
  export { FleetRolloutSchema } from './src/integration/deployments/automationSchemas';
`);
after(() => stop());
const { FleetRollouts, FleetMaxItems, FleetRolloutSchema } = fleets;

const item = (position, product, status, extra = {}) => ({
  position,
  product,
  target: `${product}-prod`,
  bundleId: `${product}-gh-2`,
  release: "v1.1.0",
  fromRelease: "v1.0.0",
  status,
  deploymentId: status === "waiting" || status === "not_started" ? null : `d-${position}`,
  detail: null,
  startedAt: null,
  completedAt: null,
  ...extra,
});
const fleet = (status, items, extra = {}) =>
  FleetRolloutSchema.parse({ id: "f1", actor: "max", status, createdAt: "2026-10-19T10:00:00Z", items, ...extra });
const offer = (product) => ({
  product,
  workspace: null,
  release: "v1.1.0",
  bundleId: `${product}-gh-2`,
  publishedAt: null,
  devTargetId: `${product}-dev`,
  devSucceededAt: "2026-10-19T09:00:00Z",
  prodTargetId: `${product}-prod`,
  prodRelease: "v1.0.0",
  rollbackCompatible: true,
  hasMigrations: null,
});

test("a running fleet names how far it got and the product it is on", () => {
  const running = fleet("running", [item(0, "foreverpin", "succeeded"), item(1, "ocharo", "running"), item(2, "pbn", "waiting")]);
  assert.deepEqual(
    { ...FleetRollouts.progress(running), current: FleetRollouts.progress(running).current.product },
    { done: 1, total: 3, succeeded: 1, current: "ocharo" },
  );
  assert.equal(FleetRollouts.summary(running), "1 of 3 promoted · on ocharo");
  assert.equal(FleetRollouts.canCancel(running), true);
  assert.deepEqual([...FleetRollouts.heldTargets(running)], ["ocharo-prod", "pbn-prod"]);
  assert.equal(FleetRollouts.active([fleet("failed", []), running]), running);
});

test("a cancel request shows until the product the fleet is on ends", () => {
  const stopping = fleet("running", [item(0, "foreverpin", "running"), item(1, "ocharo", "waiting")], {
    cancelRequestedBy: "max",
    cancelRequestedAt: "2026-10-19T10:01:00Z",
  });
  assert.equal(FleetRollouts.canCancel(stopping), false);
  assert.equal(FleetRollouts.summary(stopping), "0 of 2 promoted · on foreverpin · stopping after this product");
});

test("an ended fleet holds no target and says where it stopped", () => {
  const failed = fleet("failed", [item(0, "foreverpin", "succeeded"), item(1, "ocharo", "failed"), item(2, "pbn", "not_started")]);
  assert.equal(FleetRollouts.summary(failed), "Stopped at ocharo: 1 of 3 promoted");
  assert.equal(FleetRollouts.heldTargets(failed).size, 0);
  assert.equal(FleetRollouts.progress(failed).done, 2, "a product never started is not done");
  assert.equal(FleetRollouts.summary(fleet("succeeded", [item(0, "foreverpin", "succeeded")])), "All 1 promoted");
  assert.equal(FleetRollouts.summary(fleet("cancelled", [item(0, "foreverpin", "succeeded"), item(1, "ocharo", "not_started")])), "Cancelled after 1 of 2");
  assert.equal(FleetRollouts.itemLabel("not_started"), "Not started");
  assert.equal(FleetRollouts.active([failed]), null);
});

test("a fleet takes the chosen rows in the panel's order, never a held one, twenty at most", () => {
  const offers = ["foreverpin", "ocharo", "pbn"].map(offer);
  const chosen = new Set([FleetRollouts.key(offers[2]), FleetRollouts.key(offers[0]), FleetRollouts.key(offers[1])]);
  assert.deepEqual(FleetRollouts.selection(offers, chosen).map((row) => row.product), ["foreverpin", "ocharo", "pbn"]);
  assert.deepEqual(
    FleetRollouts.selection(offers, chosen, new Set(["ocharo-prod"])).map((row) => row.product),
    ["foreverpin", "pbn"],
  );
  const many = Array.from({ length: 25 }, (_, index) => offer(`p${index}`));
  assert.equal(FleetRollouts.selection(many, new Set(many.map(FleetRollouts.key))).length, FleetMaxItems);
  assert.deepEqual(FleetRollouts.selection(undefined, chosen), []);
});

test("the fleet read decodes what the host sends and tolerates a status it does not know", () => {
  const decoded = FleetRolloutSchema.parse({
    id: "f",
    actor: "max",
    status: "paused",
    createdAt: "x",
    items: [{ position: 0, product: "foreverpin", target: "foreverpin-prod", bundleId: "b", status: "skipped" }],
  });
  assert.equal(decoded.status, "failed");
  assert.equal(decoded.items[0].status, "waiting");
  assert.equal(decoded.reason, null);
  assert.equal(decoded.overrideFreeze, false);
  assert.equal(decoded.items[0].deploymentId, null);
});
