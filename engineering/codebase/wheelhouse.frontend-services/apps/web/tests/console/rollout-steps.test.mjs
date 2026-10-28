import assert from "node:assert/strict";
import { after, test } from "node:test";
import { stop } from "esbuild";
import { bundleModule } from "./BundleModule.mjs";

// The real step-kind rule the rollout views mark each step with, and the sections they cut the steps into.
const { StepKind, StepKinds, DeploymentStates } = await bundleModule(
  `export { StepKind, StepKinds, DeploymentStates } from './src/domain/deployments';`,
);
after(() => stop());

test("each runner step name reads as its kind, and a name the runner adds later reads as other", () => {
  const names = [
    "Render settings", "Check target", "Snapshot data", "Pull images", "Dry-run migrations", "Start desk, jobs",
    "Start containers", "Verify services", "Publish sites", "Probe sites", "Verify end to end", "Remove unused images",
    "Remove old files", "Roll back to v0.8.4", "Restore the previous release", "Drain traffic", "Wait for health",
    "Stop api", "Restart api", "Migrate api", "Rollback gate api", "Something new",
  ];
  assert.deepEqual(names.map(StepKinds.of), [
    StepKind.RenderSettings, StepKind.CheckTarget, StepKind.Snapshot, StepKind.Pull, StepKind.Migrations, StepKind.Start,
    StepKind.Start, StepKind.Verify, StepKind.PublishSites, StepKind.ProbeSites, StepKind.VerifyEndToEnd,
    StepKind.RemoveImages, StepKind.RemoveFiles, StepKind.RollBack, StepKind.Restore, StepKind.Drain, StepKind.Wait,
    StepKind.Stop, StepKind.Restart, StepKind.Migrations, StepKind.RollbackGate, StepKind.Other,
  ]);
});

const step = (name, status = "succeeded") => ({ name, status });

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
