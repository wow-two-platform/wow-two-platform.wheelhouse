import assert from "node:assert/strict";
import { after, test } from "node:test";
import { stop } from "esbuild";
import { bundleModule } from "./BundleModule.mjs";

// The real migration rules, the state and check wire schemas, and the Check now client.
const { MigrationRules, StatusState, TargetStateSchema, MigrationCheckSchema, deploymentsApi } = await bundleModule(`
  export { MigrationRules } from './src/domain/deployments/migrations';
  export { StatusState } from './src/domain/common';
  export { TargetStateSchema, MigrationCheckSchema } from './src/integration/deployments/schemas';
  export { deploymentsApi } from './src/integration/deployments';
`);
after(() => stop());

const set = (overrides = {}) => ({
  name: "default", schema: "public", service: "canary", level: 3, newestLabel: "003-add-notes", newestKind: "expand",
  state: "current", rollbackFloor: "v0.4", release: "v0.1.5", job: null, source: "migrate", at: "2026-10-28T10:00:00Z",
  ...overrides,
});
const state = (migrations) => ({ targetId: "canary-dev", project: "canary-dev", condition: "ready", current: null,
  active: null, ...(migrations === undefined ? {} : { migrations }) });

test("the target's state carries its migration sets, and an older or unreadable shape reads as none", () => {
  const read = TargetStateSchema.parse(state({ updatedAt: "2026-10-28T10:00:00Z", sets: [set(), set({ name: "identity",
    schema: null, level: null, newestLabel: null, newestKind: null, state: null, source: "gate" })] }));
  assert.deepEqual(read.migrations.sets.map((item) => [item.name, item.level, item.newestLabel, item.state]),
    [["default", 3, "003-add-notes", "current"], ["identity", null, null, null]]);
  assert.equal(TargetStateSchema.parse(state(undefined)).migrations, undefined);
  assert.equal(TargetStateSchema.parse(state(null)).migrations, null);
  assert.equal(TargetStateSchema.parse(state({ sets: "nope" })).migrations, null, "a broken block never breaks the state");
});

test("a check decodes each item's status and detail, tolerating sets it does not show", () => {
  const check = MigrationCheckSchema.parse({
    targetId: "canary-dev", project: "canary-dev", release: "v0.1.5", checkedAt: "2026-10-28T10:00:00Z",
    items: [{ service: "canary", verb: "check", release: "v0.1.5", status: "refused", exitCode: 3, seconds: 2,
      detail: "default is behind: 004-drop-notes pending",
      sets: [{ name: "default", schema: "public", state: "behind", levelBefore: 3, levelAfter: 3, applied: [],
        rollbackFloor: "v0.4", background: [], lockWaitMs: 0 }] }],
    migrations: { updatedAt: "2026-10-28T10:00:00Z", sets: [set({ state: "behind", source: "check" })] },
  });
  assert.deepEqual([check.items[0].status, check.items[0].detail, check.items[0].sets[0].state],
    ["refused", "default is behind: 004-drop-notes pending", "behind"]);
  assert.deepEqual(MigrationCheckSchema.parse({ targetId: "canary-dev", release: null, items: [] }).items, []);
});

test("a set reads by name and schema, its state in the console's vocabulary, and its source as a word", () => {
  assert.equal(MigrationRules.nameOf(set()), "default · public");
  assert.equal(MigrationRules.nameOf(set({ name: "identity", schema: "identity" })), "identity");
  assert.equal(MigrationRules.nameOf(set({ schema: null })), "default");
  assert.deepEqual(
    ["current", "ahead-compatible", "behind", "ahead-incompatible", "drifted", "owner-conflict", "something-new", null]
      .map(MigrationRules.stateOf),
    [StatusState.Healthy, StatusState.Degraded, StatusState.Failed, StatusState.Failed, StatusState.Failed,
      StatusState.Failed, StatusState.Unknown, StatusState.Unknown],
  );
  assert.deepEqual(["ahead-compatible", "current", "owner-conflict", null].map(MigrationRules.wordOf),
    ["ahead, compatible", "current", "owner conflict", "no reading"]);
  assert.deepEqual(["migrate", "gate", "check", "later"].map(MigrationRules.sourceOf),
    ["Migrated", "Rollback gate", "Checked", "later"]);
  assert.equal(MigrationRules.spansServices([set(), set({ name: "identity" })]), false);
  assert.equal(MigrationRules.spansServices([set(), set({ service: "jobs" })]), true);
  assert.deepEqual(MigrationRules.setsOf(null), []);
});

test("a check fails on any item that did not find the database ready, and blocks only on a refusal or failure", () => {
  const item = (status) => ({ service: status, status, sets: [] });
  const check = { targetId: "canary-dev", items: ["current", "applied", "refused", "failed", "busy", "timed out"].map(item) };
  const failures = MigrationRules.failures(check);
  assert.deepEqual(failures.map((entry) => entry.status), ["refused", "failed", "busy", "timed out"]);
  assert.deepEqual(failures.map(MigrationRules.isBlocking), [true, true, false, false]);
});

test("Check now reads the target's migrations endpoint", async () => {
  const requested = [];
  const original = globalThis.fetch;
  globalThis.fetch = async (url) => {
    requested.push(url);
    return new Response(JSON.stringify({ data: { targetId: "canary-dev", release: "v0.1.5", items: [], migrations: null } }), {
      status: 200,
      headers: { "Content-Type": "application/json" },
    });
  };
  try {
    const result = await deploymentsApi.checkMigrations("canary-dev");
    assert.deepEqual(requested, ["/api/deployments/targets/canary-dev/migrations"]);
    assert.deepEqual([result.ok, result.value.items, result.value.migrations], [true, [], null]);
  } finally {
    globalThis.fetch = original;
  }
});
