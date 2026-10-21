import assert from "node:assert/strict";
import { after, afterEach, test } from "node:test";
import { stop } from "esbuild";
import { bundleModule } from "./BundleModule.mjs";

const { ReleaseSets, shortRelease, deploymentsApi, productsApi, DeploymentItemSchema, DeploymentPromotionSchema } =
  await bundleModule(`
  export { ReleaseSets } from './src/domain/deployments/ReleaseSets';
  export { shortRelease } from './src/domain/portfolio/EnvironmentPill';
  export { deploymentsApi } from './src/integration/deployments';
  export { productsApi } from './src/integration/products';
  export { DeploymentItemSchema, DeploymentPromotionSchema } from './src/integration/deployments/historySchemas';
`);
const originalFetch = globalThis.fetch;
afterEach(() => {
  globalThis.fetch = originalFetch;
});
after(() => stop());

const json = (data, status = 200) =>
  new Response(JSON.stringify(data), { status, headers: { "Content-Type": "application/json" } });

test("a release set's label reads as each repository's release, and every other label as it is", () => {
  const label = "platform-v0.1.8_studio-v0.1.6_assets-sha-abcdef1";
  assert.deepEqual(ReleaseSets.parts(label), [
    { name: "platform", release: "v0.1.8" }, { name: "studio", release: "v0.1.6" }, { name: "assets", release: "sha-abcdef1" },
  ]);
  assert.equal(ReleaseSets.text(label), "platform v0.1.8 + studio v0.1.6 + assets sha-abcdef1");
  assert.equal(ReleaseSets.text("brand-desk-v0.1.0-rc.1_studio-v1.0.0"), "brand-desk v0.1.0-rc.1 + studio v1.0.0");
  assert.equal(ReleaseSets.isSet(label), true);
  assert.equal(ReleaseSets.isSet("set-0123456789ab"), true);
  for (const other of ["v1.2.0", "sha-abcdef1", "platform-v0.1.8", "platform_v1", "a-v1.0.0_a-v1.0.1", null])
    assert.equal(ReleaseSets.isSet(other), false, String(other));
  assert.equal(ReleaseSets.text("v1.2.0"), "v1.2.0");
  assert.equal(ReleaseSets.text(null), "");
});

test("a pill shows a set's first repository and how many more", () => {
  assert.equal(shortRelease("platform-v0.1.8_studio-v0.1.6_assets-v0.1.0"), "platform 0.1.8 +2");
  assert.equal(shortRelease("set-0123456789ab"), "set-0123456789ab");
  assert.equal(shortRelease("v1.2.0+abc"), "1.2.0");
});

test("releases, history items and promotions carry a set's components; a product lists its repositories", async () => {
  const component = { name: "studio", repository: "ocharo-hq/ocharo-studio", id: "ocharo-gh-21", release: "v0.1.6",
    kind: "release", commit: "c".repeat(40), publishedAt: "2026-10-20T10:00:00Z" };
  globalThis.fetch = async (url) => url === "/api/deployments/releases"
    ? json({ data: [{ id: "ocharo-set-0123456789ab", product: "ocharo", release: "platform-v0.1.8_studio-v0.1.6",
      kind: "release", prerelease: false, publishedAt: "2026-10-20T10:00:00Z", provider: "ReleaseSet",
      components: [component] }] })
    : json({ data: [{ id: "p-1", slug: "ocharo", name: "Ocharo", description: "", homepage: null,
      identity: { source: "record", commit: null, problem: null, services: [] }, workspaceId: null, status: "active",
      deploy: "wheelhouse", inactiveServices: [], repository: { name: "ocharo-hq/ocharo-platform", url: "u", defaultBranch: "main" },
      release: null, iconUrl: "/i", environments: [] },
      { id: "p-2", slug: "older", name: "Older", description: "", homepage: null,
      identity: { source: "record", commit: null, problem: null, services: [] }, workspaceId: null, status: "active",
      inactiveServices: [], repository: { name: "owner/older", url: "https://github.com/owner/older", defaultBranch: "main" },
      release: null, iconUrl: "/i", environments: [],
      repositories: undefined }] });
  const releases = await deploymentsApi.listReleases();
  assert.deepEqual(releases.value[0].components, [{ name: "studio", repository: "ocharo-hq/ocharo-studio", release: "v0.1.6",
    kind: "release", sourceCommit: "c".repeat(40) }]);
  const products = await productsApi.listProducts();
  assert.deepEqual(products.value[1].repositories, [{ name: "older", repository: "owner/older",
    url: "https://github.com/owner/older", release: null }], "a host before decision 70 reads as one repository");
  const item = DeploymentItemSchema.parse({ id: "j", status: "succeeded", outcome: "succeeded", release: "platform-v0.1.8_studio-v0.1.6",
    components: [{ name: "platform", repository: "ocharo-hq/ocharo-platform", release: "v0.1.8", kind: "release",
      sourceCommit: "a".repeat(40), services: ["desk"] }], submittedAt: "2026-10-20T10:00:00Z" });
  assert.equal(item.components[0].sourceCommit, "a".repeat(40));
  assert.equal(DeploymentItemSchema.parse({ id: "j", status: "succeeded", outcome: "succeeded", submittedAt: "x" }).components, null);
  const promotion = DeploymentPromotionSchema.parse({ product: "ocharo", release: "platform-v0.1.8_studio-v0.1.6",
    bundleId: "ocharo-set-0123456789ab", devTargetId: "ocharo-dev", devSucceededAt: "x", prodTargetId: "ocharo-prod",
    components: [{ name: "studio", release: "v0.1.6" }] });
  assert.equal(promotion.components[0].name, "studio");
});

test("branches, commits and builds name one of a product's repositories when asked", async () => {
  const requests = [];
  globalThis.fetch = async (url, options) => {
    requests.push([url, options?.body ? JSON.parse(options.body) : null]);
    return url.includes("/builds") ? json({ data: { product: "ocharo", commit: "c".repeat(40), status: "requested" } }, 202)
      : json({ data: [] });
  };
  await deploymentsApi.listBranches("ocharo", undefined, "ocharo-hq/ocharo-studio");
  await deploymentsApi.listCommits("ocharo", "main", undefined, "ocharo-hq/ocharo-studio");
  await deploymentsApi.requestBuild("ocharo", "c".repeat(40), undefined, "ocharo-hq/ocharo-studio");
  await deploymentsApi.listBranches("ocharo");
  assert.deepEqual(requests, [
    ["/api/deployments/products/ocharo/branches?repository=ocharo-hq%2Focharo-studio", null],
    ["/api/deployments/products/ocharo/commits?branch=main&repository=ocharo-hq%2Focharo-studio", null],
    ["/api/deployments/products/ocharo/builds", { commit: "c".repeat(40), repository: "ocharo-hq/ocharo-studio" }],
    ["/api/deployments/products/ocharo/branches", null],
  ]);
});
