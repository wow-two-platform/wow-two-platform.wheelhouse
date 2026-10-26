import assert from "node:assert/strict";
import { after, test } from "node:test";
import { stop } from "esbuild";
import { bundleModule } from "./BundleModule.mjs";

// The real rules of automatic deploys and freezes, and their wire decoding.
const automation = await bundleModule(`
  export { DefaultAutomation, DeployFreezes, rollbackRisks, DeploymentIssues, automationLockOf, DevCheckRules } from './src/domain/deployments';
  export { BuildFailureSchema, DeploymentAutomationSchema, DeployFreezeSchema, GitHubAccessIssueSchema, InventoryExportIssueSchema, ReleaseChangelogSchema } from './src/integration/deployments/automationSchemas';
  export { deploymentAutomationApi } from './src/integration/deployments/automationApi';
  export { DeploymentPromotionSchema, DeploymentItemSchema } from './src/integration/deployments/historySchemas';
`);
after(() => stop());
const { DefaultAutomation, DeployFreezes, DeploymentAutomationSchema, DeployFreezeSchema, ReleaseChangelogSchema, rollbackRisks } = automation;

const freeze = (product, environment, reason = null) => ({
  id: `${product}-${environment}`,
  scope: product === null ? "portfolio" : environment === null ? "product" : "environment",
  product,
  environment,
  reason,
  actor: "max",
  at: "2026-10-18T10:00:00Z",
  covers: product === null ? "every product" : environment === null ? product : `${product} ${environment}`,
});

test("a product takes auto-deploy to dev on and auto-promote off until the host says otherwise", () => {
  assert.deepEqual(DefaultAutomation("foreverpin"),
    { product: "foreverpin", autoDeployDev: true, autoPromote: false, ciDeployed: false, deploy: "wheelhouse" });
});

test("the automatic choices lock for a product its own CI or nothing deploys, whatever its slug", () => {
  assert.equal(automation.automationLockOf({ deploy: "wheelhouse" }), null);
  assert.equal(automation.automationLockOf({ deploy: "ci" }), "Its own CI deploys it.");
  assert.equal(automation.automationLockOf({ deploy: "none" }), "Nothing deploys it.");
  const decoded = DeploymentAutomationSchema.parse({
    products: [
      { product: "wheelhouse", autoDeployDev: true, autoPromote: false, ciDeployed: false, deploy: "wheelhouse" },
      { product: "notes", autoDeployDev: false, autoPromote: false, ciDeployed: false, deploy: "none" },
      // A host before the stored mode names the CI flag only.
      { product: "legacy", autoDeployDev: false, autoPromote: false, ciDeployed: true },
    ],
  });
  assert.deepEqual(decoded.products.map((item) => item.deploy), ["wheelhouse", "none", "ci"]);
});

test("the narrowest freeze covers a target: its environment, then its product, then everything", () => {
  const all = [freeze(null, null), freeze("foreverpin", null), freeze("foreverpin", "prod", "billing")];
  assert.equal(DeployFreezes.covering(all, "foreverpin", "prod").covers, "foreverpin prod");
  assert.equal(DeployFreezes.covering(all, "foreverpin", "dev").covers, "foreverpin");
  assert.equal(DeployFreezes.covering(all, "ocharo", "dev").covers, "every product");
  assert.equal(DeployFreezes.covering([freeze("foreverpin", "prod")], "foreverpin", "dev"), null);
  assert.equal(DeployFreezes.covering(undefined, "foreverpin", "dev"), null);
  assert.equal(DeployFreezes.of(all, "foreverpin", null).covers, "foreverpin");
  assert.equal(DeployFreezes.describe(all[2]), "Frozen (foreverpin prod): billing");
  assert.equal(DeployFreezes.describe(all[0]), "Frozen (every product)");
});

test("the automation and freeze reads decode what the host sends", () => {
  const decoded = DeploymentAutomationSchema.parse({
    products: [{ product: "wheelhouse", autoDeployDev: false, autoPromote: false, ciDeployed: true }],
    queue: [{ targetId: "foreverpin-dev", product: "foreverpin", environment: "dev", release: "v1.2.0", bundleId: "foreverpin-gh-3", queuedAt: "2026-10-18T10:00:00Z", cause: "something-new" }],
  });
  assert.equal(decoded.queue[0].cause, "auto_dev");
  assert.equal(decoded.queue[0].publishedAt, null);
  const parsed = DeployFreezeSchema.parse({ id: "f", scope: "environment", product: "foreverpin", environment: "prod", actor: "max", at: "x", covers: "foreverpin prod" });
  assert.equal(parsed.reason, null);
});

test("a release that cannot roll back automatically or carries migrations names each risk", () => {
  assert.deepEqual(rollbackRisks({ rollbackCompatible: false, hasMigrations: true }), ["Can't roll back automatically", "Has migrations"]);
  assert.deepEqual(rollbackRisks({ rollbackCompatible: true, migrations: null }), []);
  assert.deepEqual(rollbackRisks({ rollbackCompatible: null, hasMigrations: null }), []);
  assert.deepEqual(rollbackRisks(null), []);
  const offer = automation.DeploymentPromotionSchema.parse({
    product: "foreverpin", release: "v1.1.0", bundleId: "b", devTargetId: "d", devSucceededAt: "x", prodTargetId: "p",
  });
  assert.equal(offer.rollbackCompatible, null);
  assert.equal(automation.DeploymentItemSchema.parse({ id: "x", status: "succeeded", outcome: "succeeded", submittedAt: "x" }).deployReason, null);
});

test("a promotion's dev check reads passed, failed, not run or none declared, and holds auto-promote as the host does", () => {
  const { DevCheckRules } = automation;
  const ran = (status, fields = {}) => ({
    status, service: "api", completedAt: "x", seconds: 12, exitCode: status === "passed" ? 0 : 3,
    detail: status === "passed" ? "Passed" : "Exited 3", summary: null, ...fields,
  });
  const read = (verification, verifies) => {
    const reading = DevCheckRules.of({ verification, verifies });
    return [reading.state, reading.status, reading.word, reading.holdsAutoPromote];
  };
  assert.deepEqual(read(ran("passed"), true), ["passed", "succeeded", "passed", false]);
  assert.deepEqual(read(ran("failed"), true), ["failed", "failed", "failed", true]);
  assert.deepEqual(read(null, true), ["not_run", "none", "not run", true]);
  assert.deepEqual(read(null, false), ["not_declared", "unknown", "no check declared", false]);
  assert.deepEqual(read(null, null), ["not_declared", "unknown", "no check declared", false],
    "an unknown bundle clears auto-promote, as the host's gate does");
  assert.equal(DevCheckRules.of({ verification: ran("failed", { summary: "checkout returned 500" }), verifies: true }).cause,
    "Exited 3 · in api · 12s: checkout returned 500");
  assert.equal(DevCheckRules.of({ verification: ran("passed"), verifies: true }).cause, "Passed · in api · 12s");
  assert.equal(DevCheckRules.of({ verification: ran("failed", { detail: "Timed out after 300s", exitCode: null }), verifies: true }).state,
    "failed");

  const decode = (fields) => automation.DeploymentPromotionSchema.parse({
    product: "foreverpin", release: "v1.1.0", bundleId: "b", devTargetId: "d", devSucceededAt: "x", prodTargetId: "p", ...fields,
  });
  const bare = decode({});
  assert.deepEqual([bare.verifies, bare.verification], [null, null]);
  const checked = decode({ verifies: true, verification: { status: "passed", service: "api", seconds: 9 } });
  assert.deepEqual([checked.verification.status, checked.verification.summary, checked.verification.exitCode], ["passed", null, null]);
  assert.equal(decode({ verifies: true, verification: { status: "skipped" } }).verification.status, "failed",
    "a status the console does not know reads as failed");
});

test("a changelog keeps release bodies as text and only GitHub links", () => {
  const parsed = ReleaseChangelogSchema.parse({
    to: "v1.1.0",
    releases: [{ release: "v1.1.0", name: "v1.1.0", publishedAt: "x", body: "<script>alert(1)</script>", url: "https://github.com/o/p/releases/tag/v1.1.0" }],
    commits: null,
  });
  assert.equal(parsed.releases[0].body, "<script>alert(1)</script>");
  assert.deepEqual(parsed.migrations, []);
  assert.deepEqual(parsed.notYet, [], "a host before partial release sets names none");
  assert.deepEqual(ReleaseChangelogSchema.parse({ to: "platform-v0.1.9", notYet: [{ name: "marketing", repository: "o/m" }] }).notYet,
    [{ name: "marketing", repository: "o/m" }]);
  assert.throws(() =>
    ReleaseChangelogSchema.parse({ to: "v1", releases: [{ release: "v1", name: "v1", publishedAt: "x", url: "javascript:alert(1)" }] }),
  );
});

test("a rollout that regressed while watched names its first finding", () => {
  const deployment = automation.DeploymentItemSchema.parse({
    id: "x", status: "succeeded", outcome: "succeeded", submittedAt: "x",
    watch: { status: "degraded", findings: ["api restarted 2 times since the deploy", "Site app stopped answering"] },
  });
  assert.equal(automation.DeploymentIssues.headline({ kind: "degraded", deployment }), "Regressed after the deploy: api restarted 2 times since the deploy");
  assert.equal(automation.DeploymentItemSchema.parse({ id: "x", status: "succeeded", outcome: "succeeded", submittedAt: "x", watch: { status: "odd" } }).watch.status, "unverified");
});

test("a GitHub access problem names its owner, or the App itself when it has none", () => {
  const owner = automation.GitHubAccessIssueSchema.parse({ owner: "ocharo-hq", cause: "Not installed.", status: 404, at: "2026-10-19T09:00:00Z" });
  assert.deepEqual([owner.owner, owner.status], ["ocharo-hq", 404]);
  const app = automation.GitHubAccessIssueSchema.parse({ cause: "The key is missing.", at: "2026-10-19T09:00:00Z" });
  assert.deepEqual([app.owner, app.status], [null, null]);
  assert.throws(() => automation.GitHubAccessIssueSchema.parse({ owner: "ocharo-hq", at: "2026-10-19T09:00:00Z" }));
});

test("a failed inventory export names its cause and time, and the list is empty once an export succeeds", async () => {
  const issue = automation.InventoryExportIssueSchema.parse({ cause: "The snapshot file could not be written.", at: "2026-10-26T09:00:00Z" });
  assert.deepEqual(issue, { cause: "The snapshot file could not be written.", at: "2026-10-26T09:00:00Z" });
  assert.throws(() => automation.InventoryExportIssueSchema.parse({ at: "2026-10-26T09:00:00Z" }));

  const requested = [];
  const answers = [[{ cause: "The snapshot file could not be written.", at: "2026-10-26T09:00:00Z" }], []];
  const original = globalThis.fetch;
  globalThis.fetch = async (url) => {
    requested.push(url);
    return new Response(JSON.stringify({ data: answers[requested.length - 1] }), {
      status: 200,
      headers: { "Content-Type": "application/json" },
    });
  };
  try {
    const failed = await automation.deploymentAutomationApi.listInventoryExportIssues();
    const recovered = await automation.deploymentAutomationApi.listInventoryExportIssues();
    assert.deepEqual(requested, ["/api/deployments/issues/inventory", "/api/deployments/issues/inventory"]);
    assert.deepEqual([failed.ok, failed.value.length, failed.value[0].cause], [true, 1, "The snapshot file could not be written."]);
    assert.deepEqual([recovered.ok, recovered.value.length], [true, 0]);
  } finally {
    globalThis.fetch = original;
  }
});

test("a failed build links only to GitHub", () => {
  const parsed = automation.BuildFailureSchema.parse({ product: "foreverpin", runId: 9, url: "https://github.com/o/p/actions/runs/9" });
  assert.equal(parsed.conclusion, "failure");
  assert.throws(() => automation.BuildFailureSchema.parse({ product: "foreverpin", runId: 9, url: "https://evil.example/run" }));
});
