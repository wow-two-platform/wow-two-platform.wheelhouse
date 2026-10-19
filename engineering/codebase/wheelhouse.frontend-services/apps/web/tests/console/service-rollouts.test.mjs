import assert from "node:assert/strict";
import { after, test } from "node:test";
import { stop } from "esbuild";
import { bundleModule } from "./BundleModule.mjs";

// The real reading of a rollout's per-service outcomes and its wire decoding.
const module = await bundleModule(`
  export { ServiceRollouts } from './src/domain/deployments';
  export { DeploymentDetailSchema } from './src/integration/deployments/historySchemas';
`);
after(() => stop());
const { ServiceRollouts, DeploymentDetailSchema } = module;

const rollout = {
  staged: true,
  waves: [["db"], ["api"]],
  removed: [],
  services: [
    { name: "db", change: "updated", outcome: "rolled_back", dependsOn: [] },
    { name: "api", change: "updated", outcome: "not_started", dependsOn: ["db"] },
    { name: "web", change: "unchanged", outcome: "unchanged", dependsOn: [] },
    { name: "worker", change: "added", outcome: "updated", dependsOn: [] },
  ],
};

test("each service reads how the rollout treated it and the wave it started in", () => {
  assert.deepEqual(ServiceRollouts.outcome(rollout, "db"), { label: "rolled back", tone: "warning", wave: 1 });
  assert.deepEqual(ServiceRollouts.outcome(rollout, "api"), { label: "not started", tone: "warning", wave: 2 });
  assert.deepEqual(ServiceRollouts.outcome(rollout, "web"), { label: "kept running", tone: "neutral", wave: null });
  assert.deepEqual(ServiceRollouts.outcome(rollout, "worker"), { label: "started", tone: "success", wave: null });
  assert.equal(ServiceRollouts.outcome(rollout, "missing"), null);
  assert.equal(ServiceRollouts.outcome(null, "db"), null);
  assert.equal(ServiceRollouts.outcome({ ...rollout, staged: false }, "db").wave, null, "one Compose up has no waves");
});

test("a detail without per-service outcomes decodes to none", () => {
  const detail = DeploymentDetailSchema.parse({
    deployment: { id: "d", status: "succeeded", outcome: "succeeded", submittedAt: "2026-10-19T10:00:00Z" },
  });
  assert.equal(detail.serviceRollout, null);
  const reported = DeploymentDetailSchema.parse({
    deployment: { id: "d", status: "succeeded", outcome: "succeeded", submittedAt: "2026-10-19T10:00:00Z" },
    serviceRollout: { waves: [["api"]], services: [{ name: "api", change: "updated", outcome: "updated" }] },
  });
  assert.equal(reported.serviceRollout.staged, false);
  assert.deepEqual(reported.serviceRollout.services[0].dependsOn, []);
});
