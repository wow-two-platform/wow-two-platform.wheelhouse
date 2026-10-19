import assert from "node:assert/strict";
import { after, test } from "node:test";
import { stop } from "esbuild";
import { bundleModule } from "./BundleModule.mjs";

// The real rules of automatic deploys and freezes, and their wire decoding.
const automation = await bundleModule(`
  export { DefaultAutomation, DeployFreezes, rollbackRisks, DeploymentIssues, automationLockOf } from './src/domain/deployments';
  export { BuildFailureSchema, DeploymentAutomationSchema, DeployFreezeSchema, ReleaseChangelogSchema } from './src/integration/deployments/automationSchemas';
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

test("a changelog keeps release bodies as text and only GitHub links", () => {
  const parsed = ReleaseChangelogSchema.parse({
    to: "v1.1.0",
    releases: [{ release: "v1.1.0", name: "v1.1.0", publishedAt: "x", body: "<script>alert(1)</script>", url: "https://github.com/o/p/releases/tag/v1.1.0" }],
    commits: null,
  });
  assert.equal(parsed.releases[0].body, "<script>alert(1)</script>");
  assert.deepEqual(parsed.migrations, []);
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

test("a failed build links only to GitHub", () => {
  const parsed = automation.BuildFailureSchema.parse({ product: "foreverpin", runId: 9, url: "https://github.com/o/p/actions/runs/9" });
  assert.equal(parsed.conclusion, "failure");
  assert.throws(() => automation.BuildFailureSchema.parse({ product: "foreverpin", runId: 9, url: "https://evil.example/run" }));
});
