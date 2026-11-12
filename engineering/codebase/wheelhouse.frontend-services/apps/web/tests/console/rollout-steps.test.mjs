import assert from "node:assert/strict";
import { after, test } from "node:test";
import { stop } from "esbuild";
import { bundleModule } from "./BundleModule.mjs";

// The real step-kind rule the rollout views mark each step with, and the sections they cut the steps into.
const { StepKind, StepKinds, DeploymentStates, DeploymentJobSchema, DeploymentDetailSchema } = await bundleModule(`
  export { StepKind, StepKinds, DeploymentStates } from './src/domain/deployments';
  export { DeploymentJobSchema } from './src/integration/deployments/schemas';
  export { DeploymentDetailSchema } from './src/integration/deployments/historySchemas';
`);
after(() => stop());

test("each runner step name reads as its kind, and a name the runner adds later reads as other", () => {
  const names = [
    "Render settings", "Check target", "Snapshot data", "Pull images", "Dry-run migrations", "Start desk, jobs",
    "Start containers", "Verify services", "Publish sites", "Probe sites", "Verify end to end", "Remove unused images",
    "Remove old files", "Roll back to v0.8.4", "Restore the previous release", "Drain traffic", "Wait for health",
    "Stop api", "Restart api", "Migrate api", "Rollback gate api", "Snapshot the database", "Stop the environment",
    "Reset ocharo_dev", "Migration levels to 0", "Reset identity to level 0", "Migration level to 0", "Something new",
    "Config check identity", "Rollback gate config check identity", "Rollback gate config check",
  ];
  assert.deepEqual(names.map(StepKinds.of), [
    StepKind.RenderSettings, StepKind.CheckTarget, StepKind.Snapshot, StepKind.Pull, StepKind.Migrations, StepKind.Start,
    StepKind.Start, StepKind.Verify, StepKind.PublishSites, StepKind.ProbeSites, StepKind.VerifyEndToEnd,
    StepKind.RemoveImages, StepKind.RemoveFiles, StepKind.RollBack, StepKind.Restore, StepKind.Drain, StepKind.Wait,
    StepKind.Stop, StepKind.Restart, StepKind.Migrations, StepKind.RollbackGate, StepKind.Snapshot, StepKind.Stop,
    StepKind.Reset, StepKind.Migrations, StepKind.Reset, StepKind.Migrations, StepKind.Other,
    StepKind.ConfigCheck, StepKind.ConfigCheck, StepKind.ConfigCheck,
  ]);
});

const step = (name, status = "succeeded") => ({ name, status });

test("a copy's steps read as a check, a snapshot, a stop, the copy itself, a verify and the migration levels", () => {
  const names = ["Check the copy", "Snapshot the database", "Stop the environment", "Dump harvestadapters_dev",
    "Copy into harvest_dev", "Compare row counts", "Migration levels to behind"];
  assert.deepEqual(names.map(StepKinds.of), [StepKind.CheckTarget, StepKind.Snapshot, StepKind.Stop, StepKind.Copy,
    StepKind.Copy, StepKind.Verify, StepKind.Migrations]);
  assert.deepEqual([...StepKinds.restoreSteps(names.map((name) => step(name, name === "Compare row counts" ? "failed" : "succeeded")))], [],
    "a copy's steps never read as an automatic restore");
});

test("a set reset's steps never read as an automatic restore", () => {
  const failed = [step("Snapshot the database"), step("Stop identity"), step("Reset identity to level 0", "failed")];
  assert.deepEqual([...StepKinds.restoreSteps(failed)], []);
});

test("a rollback gate after a failure belongs to the automatic restore; one before any failure stands alone", () => {
  const restoring = [
    step("Pull images"), step("Migrate api"), step("Start api"), step("Verify services", "failed"),
    step("Rollback gate api"), step("Roll back to v0.1.4"), step("Restore api"),
  ];
  assert.deepEqual([...StepKinds.restoreSteps(restoring)].map((item) => item.name),
    ["Rollback gate api", "Roll back to v0.1.4", "Restore api"]);

  const olderRelease = [step("Pull images"), step("Rollback gate api"), step("Start api"), step("Verify services")];
  assert.deepEqual([...StepKinds.restoreSteps(olderRelease)], []);
});

test("a rollback gate before any change stays before the boundary; a migrate step is a change", () => {
  const sections = DeploymentStates.sections([
    step("Check target"), step("Pull images"), step("Rollback gate api"), step("Migrate api"), step("Start api"),
    step("Publish sites"),
  ]);
  assert.deepEqual(
    [sections.before.map((item) => item.name), sections.changes.map((item) => item.name)],
    [["Check target", "Pull images", "Rollback gate api"], ["Migrate api", "Start api"]],
  );
});

test("a config check gate after a failure belongs to the restore; a refused item's sibling checks stand alone", () => {
  const restoring = [
    step("Pull images"), step("Migrate api"), step("Config check api"), step("Start api", "failed"),
    step("Rollback gate api"), step("Rollback gate config check api"), step("Roll back to v0.1.4"),
  ];
  assert.deepEqual([...StepKinds.restoreSteps(restoring)].map((item) => item.name),
    ["Rollback gate api", "Rollback gate config check api", "Roll back to v0.1.4"]);

  // Every item runs even after one refuses; the next item checks the release deployed, never a restore.
  const refused = [step("Pull images"), step("Config check api", "failed"), step("Config check worker")];
  assert.deepEqual([...StepKinds.restoreSteps(refused)], []);
});

test("a config check changes nothing, so it stays before the boundary unless a migrate step came first", () => {
  const checked = DeploymentStates.sections([
    step("Check target"), step("Pull images"), step("Config check api"), step("Start api"), step("Publish sites"),
  ]);
  assert.deepEqual([checked.before.map((item) => item.name), checked.changes.map((item) => item.name)],
    [["Check target", "Pull images", "Config check api"], ["Start api"]]);
  const migrated = DeploymentStates.sections([step("Pull images"), step("Migrate api"), step("Config check api")]);
  assert.deepEqual(migrated.changes.map((item) => item.name), ["Migrate api", "Config check api"]);
});

test("a config check step's findings decode by setting and code, and a malformed one is dropped", () => {
  const findings = [
    { kind: "invalid", setting: "Identity:Password:MinLength", code: "range" },
    { kind: "unknown", setting: "Identity:SignUp:Enabled" },
    { kind: "stranded", setting: "Identity:Methods:Password:IsEnabled", code: "only-method-removed", count: 3 },
    { kind: "melted", setting: "Odd:Kind" },
    "not a finding",
  ];
  const expected = [
    { kind: "invalid", setting: "Identity:Password:MinLength", code: "range", count: null },
    { kind: "unknown", setting: "Identity:SignUp:Enabled", code: null, count: null },
    { kind: "stranded", setting: "Identity:Methods:Password:IsEnabled", code: "only-method-removed", count: 3 },
  ];
  const job = DeploymentJobSchema.parse({ id: "x", status: "failed", steps: [
    { name: "Pull images", status: "succeeded" },
    { name: "Config check identity", status: "failed", detail: "Refused (exit 1): 3 findings in 1s", findings },
  ] });
  assert.equal(job.steps[0].findings, undefined);
  assert.deepEqual(job.steps[1].findings, expected);
  const detail = DeploymentDetailSchema.parse({
    deployment: { id: "x", status: "failed", outcome: "failed", submittedAt: "2026-11-02T10:00:00Z" },
    steps: [{ name: "Config check identity", status: "failed", findings }, { name: "Odd", status: "failed", findings: 7 }],
  });
  assert.deepEqual(detail.steps[0].findings, expected);
  assert.equal(detail.steps[1].findings, undefined);
});
