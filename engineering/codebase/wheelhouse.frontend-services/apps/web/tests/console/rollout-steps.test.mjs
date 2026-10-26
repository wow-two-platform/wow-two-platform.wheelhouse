import assert from "node:assert/strict";
import { after, test } from "node:test";
import { stop } from "esbuild";
import { bundleModule } from "./BundleModule.mjs";

// The real step-kind rule the rollout views mark each step with.
const { StepKind, StepKinds } = await bundleModule(`export { StepKind, StepKinds } from './src/domain/deployments';`);
after(() => stop());

test("each runner step name reads as its kind, and a name the runner adds later reads as other", () => {
  const names = [
    "Render settings", "Check target", "Snapshot data", "Pull images", "Dry-run migrations", "Start desk, jobs",
    "Start containers", "Verify services", "Publish sites", "Probe sites", "Verify end to end", "Remove unused images",
    "Remove old files", "Roll back to v0.8.4", "Restore the previous release", "Drain traffic", "Wait for health",
    "Stop api", "Restart api", "Something new",
  ];
  assert.deepEqual(names.map(StepKinds.of), [
    StepKind.RenderSettings, StepKind.CheckTarget, StepKind.Snapshot, StepKind.Pull, StepKind.Migrations, StepKind.Start,
    StepKind.Start, StepKind.Verify, StepKind.PublishSites, StepKind.ProbeSites, StepKind.VerifyEndToEnd,
    StepKind.RemoveImages, StepKind.RemoveFiles, StepKind.RollBack, StepKind.Restore, StepKind.Drain, StepKind.Wait,
    StepKind.Stop, StepKind.Restart, StepKind.Other,
  ]);
});
