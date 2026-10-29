import assert from "node:assert/strict";
import { after, test } from "node:test";
import { stop } from "esbuild";
import { bundleModule } from "./BundleModule.mjs";
import { container, target } from "./LoadModule.mjs";

// The real control rules, the pill and health rules a stopped environment reads through, and the wire schemas.
const {
  ControlOperation, ServiceControls, EnvironmentPillRules, PillState, EnvironmentHealth, EnvironmentHealthRules,
  AttentionRules, describeServiceObservation, TargetStateSchema, DeploymentItemSchema, PortfolioSummarySchema,
  ControlSubmittedSchema,
} = await bundleModule(`
  export { ControlOperation, ServiceControls } from './src/domain/deployments/controls';
  export { EnvironmentPillRules, PillState } from './src/domain/portfolio/EnvironmentPill';
  export { EnvironmentHealth, EnvironmentHealthRules } from './src/domain/overview/EnvironmentHealth';
  export { AttentionRules } from './src/domain/overview/AttentionRules';
  export { describeServiceObservation } from './src/presentation/product/serviceMap/ServiceObservation';
  export { ControlSubmittedSchema, TargetStateSchema } from './src/integration/deployments/schemas';
  export { DeploymentItemSchema } from './src/integration/deployments/historySchemas';
  export { PortfolioSummarySchema } from './src/integration/portfolio/schemas';
`);
after(() => stop());

const dev = { id: "foreverpin-dev", environment: "dev", product: "foreverpin" };
const prod = { id: "foreverpin-prod", environment: "prod", product: "foreverpin" };

test("a running service stops or restarts; a stopped one starts; a stopped environment starts whole", () => {
  assert.deepEqual(ServiceControls.forService(null, "api"), [ControlOperation.Restart, ControlOperation.Stop]);
  assert.deepEqual(ServiceControls.forService({ environment: false, services: ["api"] }, "api"), [ControlOperation.Start]);
  assert.deepEqual(ServiceControls.forService({ environment: true, services: [] }, "api"), []);
  assert.equal(ServiceControls.forEnvironment(null), ControlOperation.Stop);
  assert.equal(ServiceControls.forEnvironment({ environment: false, services: ["api"] }), ControlOperation.Start);
});

test("Wheelhouse never offers controls on itself, and only prod asks for a typed ID and a reason", () => {
  assert.equal(ServiceControls.offers("wheelhouse", "wheelhouse"), false);
  assert.equal(ServiceControls.offers("foreverpin", "wheelhouse"), true);
  assert.equal(ServiceControls.isGuarded(prod), true);
  assert.equal(ServiceControls.isGuarded(dev), false);
});

test("a control reads by what it does", () => {
  assert.equal(ServiceControls.label(ControlOperation.Stop, "management"), "Stop management");
  assert.equal(ServiceControls.labelOf({ kind: "start", services: [] }), "Start the environment");
  assert.equal(ServiceControls.stateLabel({ environment: false, services: ["management"] }), "management stopped");
  assert.match(ServiceControls.consequence({ target: dev, operation: "stop", service: null }), /volumes and routes stay/);
});

test("a database reset takes the whole environment and says what it drops and what rebuilds it", () => {
  assert.equal(ControlOperation.Reset, "reset");
  assert.equal(ServiceControls.label(ControlOperation.Reset, null), "Reset the database");
  assert.equal(ServiceControls.labelOf({ kind: "reset", services: [] }), "Reset the database");
  assert.equal(ServiceControls.verb(ControlOperation.Reset), "Reset");
  const consequence = ServiceControls.consequence({ target: prod, operation: ControlOperation.Reset, service: null });
  assert.match(consequence, /^foreverpin-prod's database is snapshotted, then dropped and created again, empty/);
  assert.match(consequence, /next deploy's migrate step builds the schema/);
  assert.equal(ServiceControls.isGuarded(prod), true, "prod's reset asks for its target ID typed and a reason");
  const item = DeploymentItemSchema.parse({ id: "r1", status: "succeeded", outcome: "succeeded", state: "succeeded",
    attention: "none", submittedAt: "2026-10-29T10:00:00Z", operation: { kind: "reset", services: [] } });
  assert.deepEqual(item.operation, { kind: "reset", services: [] });
  const queued = ControlSubmittedSchema.parse({ id: "r1", targetId: "foreverpin-dev", status: "queued",
    operation: { kind: "reset", services: [] }, release: "v1.0.0" });
  assert.equal(queued.operation.kind, "reset");
});

test("a stopped environment's pill reads stopped, never failed", () => {
  const summary = { environment: "dev", lastDeploy: null, running: null, weekly: [], lastAttempt: null,
    stopped: { environment: true, services: [] } };
  const pill = EnvironmentPillRules.of({ environment: "dev", reading: { environment: "dev", health: EnvironmentHealth.Failing,
    targetId: "foreverpin-dev" }, summary });
  assert.equal(pill.state, PillState.Stopped);
  const some = EnvironmentPillRules.of({ environment: "dev", reading: { environment: "dev", health: EnvironmentHealth.Healthy,
    targetId: "foreverpin-dev" }, summary: { ...summary, stopped: { environment: false, services: ["management"] } } });
  assert.deepEqual([some.state, some.detail], [PillState.Stopped, "1 stopped"]);
});

test("what the owner stopped is neither failing nor an attention item", () => {
  const exited = container({ service: "management", state: "exited", health: null, exitCode: 0 });
  const vitals = (stopped) => ({ collectedAt: "2026-10-26T10:00:00Z", servers: [], targets: [{ targetId: "foreverpin-rehearsal",
    serverId: "rehearsal", ok: true, containers: [exited], condition: "ready", release: "v1", stopped }] });
  assert.equal(EnvironmentHealthRules.ofTarget(target(), { vitals: vitals(null) }), EnvironmentHealth.Failing);
  assert.equal(EnvironmentHealthRules.ofTarget(target(), { vitals: vitals({ environment: false, services: ["management"] }) }),
    EnvironmentHealth.Healthy);
  assert.equal(EnvironmentHealthRules.ofTarget(target(), { vitals: vitals({ environment: true, services: [] }) }),
    EnvironmentHealth.Stopped);
  assert.equal(AttentionRules.fromVitals(vitals({ environment: false, services: ["management"] }))
    .some((item) => item.id.startsWith("container:")), false);
  assert.equal(describeServiceObservation("management", [exited], { environment: true, services: [] }).label, "Stopped");
});

test("the wire carries what is stopped and which items are controls", () => {
  const state = TargetStateSchema.parse({ targetId: "foreverpin-dev", project: "foreverpin-dev", condition: "ready",
    current: null, active: { id: "c1", status: "succeeded", operation: { kind: "stop", services: ["management"] } },
    stopped: { environment: false, services: ["management"], at: "2026-10-26T10:00:00Z", actor: "max" } });
  assert.deepEqual(state.stopped.services, ["management"]);
  assert.equal(state.active.operation.kind, "stop");
  const item = DeploymentItemSchema.parse({ id: "c1", status: "succeeded", outcome: "succeeded", state: "succeeded",
    attention: "none", submittedAt: "2026-10-26T10:00:00Z", operation: { kind: "restart", services: ["api"] } });
  assert.deepEqual(item.operation, { kind: "restart", services: ["api"] });
  assert.equal(DeploymentItemSchema.parse({ id: "d1", status: "succeeded", outcome: "succeeded", state: "succeeded",
    attention: "none", submittedAt: "2026-10-26T10:00:00Z" }).operation, null);
  const summary = PortfolioSummarySchema.parse({ timeZone: "UTC", weeks: [], workspaces: [], products: [{ product: "foreverpin",
    workspace: null, succeeded: 0, failed: 0, environments: [{ environment: "dev", lastDeploy: null, running: null, weekly: [],
      lastAttempt: null, stopped: { environment: true, services: [] } }] }] });
  assert.equal(summary.products[0].environments[0].stopped.environment, true);
});

test("a queued control reads back as the job its dialog follows to the end", () => {
  const queued = ControlSubmittedSchema.parse({ id: "c1", targetId: "wheelhousecanary-dev", status: "queued",
    operation: { kind: "restart", services: ["canary"] }, release: "v1.0.0" });
  assert.deepEqual([queued.id, queued.status, queued.operation.services], ["c1", "queued", ["canary"]]);
});
