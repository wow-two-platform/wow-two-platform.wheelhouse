import assert from "node:assert/strict";
import { after, test } from "node:test";
import { stop } from "esbuild";
import { bundleModule } from "./BundleModule.mjs";

// The real rules of Wheelhouse deploying itself (decision 64) and their wire decoding.
const self = await bundleModule(`
  export { SelfDeployRules, SelfProduct, FleetRollouts } from './src/domain/deployments';
  export { DeploymentTargetSchema, SelfDeploymentSchema } from './src/integration/deployments/schemas';
`);
after(() => stop());
const { SelfDeployRules, FleetRollouts, DeploymentTargetSchema, SelfDeploymentSchema } = self;

const target = (id, environment, isSelf) => ({
  id, product: isSelf ? "wheelhouse" : "foreverpin", environment, serverId: "control", provider: "ovhcloud",
  host: "172.30.0.1", acceptsCandidates: environment === "dev", needsConfirmation: isSelf && environment === "prod",
  requiresDevPass: environment === "prod", self: isSelf,
});
const offer = (product, prodTargetId, bundleId, release) => ({
  product, workspace: null, release, bundleId, publishedAt: null, devTargetId: product + "-dev",
  devSucceededAt: "2026-10-19T09:00:00Z", prodTargetId, prodRelease: "v0.3.9", rollbackCompatible: false,
  hasMigrations: null, verifies: true, verification: null,
});
const run = (status, overrides = {}) => ({
  id: "job-1", release: "v0.5.21", previousRelease: "v0.5.20", status, outcome: "pending", actor: "sulton-max",
  reason: "v0.5 trial", failure: null, submittedAt: "2026-10-19T10:00:00Z", completedAt: "2026-10-19T10:02:00Z",
  beforeBoot: true, ...overrides,
});
const reading = (latest) => ({ environment: "prod", target: "wheelhouse-prod", bootedAt: "2026-10-19T10:01:00Z", latest });
const now = new Date("2026-10-19T11:00:00Z");

test("a target names whether it runs Wheelhouse itself; an older host's targets read as not", () => {
  const decoded = DeploymentTargetSchema.parse({ ...target("foreverpin-prod", "prod", false), self: undefined });
  assert.equal(decoded.self, false);
  assert.equal(DeploymentTargetSchema.parse(target("wheelhouse-prod", "prod", true)).self, true);
});

test("only Wheelhouse's own prod takes the owner's typed, reasoned promotion; only its own console restarts", () => {
  assert.equal(SelfDeployRules.isSelfProd(target("wheelhouse-prod", "prod", true)), true);
  assert.equal(SelfDeployRules.isSelfProd(target("wheelhouse-dev", "dev", true)), false);
  assert.equal(SelfDeployRules.isSelfProd(target("foreverpin-prod", "prod", false)), false);
  assert.equal(SelfDeployRules.replacesConsole(target("wheelhouse-prod", "prod", true), "prod"), true);
  // Prod deploying its dev replaces a sibling, not the console in use.
  assert.equal(SelfDeployRules.replacesConsole(target("wheelhouse-dev", "dev", true), "prod"), false);
  assert.equal(SelfDeployRules.replacesConsole(target("wheelhouse-prod", "prod", true), null), false);
});

test("a prod target starts on the newest dev-proven release Ready to promote offers, when the list holds it", () => {
  const offers = [offer("wheelhouse", "wheelhouse-prod", "wheelhouse-v0-5-21", "v0.5.21"),
                  offer("foreverpin", "foreverpin-prod", "foreverpin-gh-9", "v1.9.0")];
  const available = [{ id: "wheelhouse-v0-4-7" }, { id: "wheelhouse-v0-5-21" }];
  assert.equal(SelfDeployRules.preselected(offers, target("wheelhouse-prod", "prod", true), available), "wheelhouse-v0-5-21");
  assert.equal(SelfDeployRules.preselected(offers, target("wheelhouse-prod", "prod", true), [{ id: "wheelhouse-v0-4-7" }]), null);
  assert.equal(SelfDeployRules.preselected(offers, target("wheelhouse-dev", "dev", true), available), null);
  assert.equal(SelfDeployRules.preselected(undefined, target("wheelhouse-prod", "prod", true), available), null);
});

test("the console that booted from a rollout of itself says how it ended, once, for a day", () => {
  const ended = SelfDeployRules.notice(reading(run("succeeded", { outcome: "succeeded" })), now, new Set());
  assert.deepEqual(ended, { id: "job-1", tone: "success", title: "Wheelhouse now runs v0.5.21",
    description: "Promoted from v0.5.20 by sulton-max: “v0.5 trial”. This console started with it." });
  const back = SelfDeployRules.notice(reading(run("rolled_back", { failure: "Site console did not answer through the ingress" })), now, new Set());
  assert.equal(back.tone, "warning");
  assert.equal(back.description, "Site console did not answer through the ingress. This console runs v0.5.20 again.");
  assert.equal(SelfDeployRules.notice(reading(run("rollback_failed")), now, new Set()).tone, "danger");
  assert.equal(SelfDeployRules.notice(reading(run("running", { completedAt: null })), now, new Set()).tone, "info");
  // Not a rollout that started this console, dismissed, a day old, or none at all: nothing to say.
  assert.equal(SelfDeployRules.notice(reading(run("succeeded", { beforeBoot: false })), now, new Set()), null);
  assert.equal(SelfDeployRules.notice(reading(run("succeeded")), now, new Set(["job-1"])), null);
  assert.equal(SelfDeployRules.notice(reading(run("succeeded")), new Date("2026-10-20T10:03:00Z"), new Set()), null);
  assert.equal(SelfDeployRules.notice(reading(null), now, new Set()), null);
  assert.equal(SelfDeployRules.notice(null, now, new Set()), null);
});

test("the read-back decodes as the host answers it", () => {
  const decoded = SelfDeploymentSchema.parse(reading(run("succeeded")));
  assert.equal(decoded.latest.previousRelease, "v0.5.20");
  assert.equal(SelfDeploymentSchema.parse({ environment: "dev", target: null, bootedAt: "2026-10-19T10:01:00Z", latest: null }).latest, null);
});

test("a fleet never takes Wheelhouse itself, which is promoted on its own", () => {
  const offers = [offer("wheelhouse", "wheelhouse-prod", "wheelhouse-v0-5-21", "v0.5.21"),
                  offer("foreverpin", "foreverpin-prod", "foreverpin-gh-9", "v1.9.0")];
  const chosen = new Set(offers.map(FleetRollouts.key));
  assert.deepEqual(FleetRollouts.selection(offers, chosen).map((item) => item.product), ["foreverpin"]);
});
