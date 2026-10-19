import assert from "node:assert/strict";
import { after, test } from "node:test";
import { resolve } from "node:path";
import { build, stop } from "esbuild";

// Bundle the real inventory form modules in memory; the SDK forms engine comes from the installed package.
const bundle = await build({
  stdin: {
    contents: `
      export * from './src/application/products/ProductForms';
      export * from './src/application/targets/TargetForms';
      export * from './src/application/servers/ServerForms';
      export { vaultDefinitionFormOf, vaultDefinitionFormSchema, vaultDefinitionRequestOf } from './src/application/secrets/VaultDefinitionForms';
      export * from './src/application/workspaces/WorkspaceForms';
    `,
    resolveDir: process.cwd(),
  },
  alias: { "@": resolve("src") },
  bundle: true,
  platform: "node",
  format: "esm",
  write: false,
});
const forms = await import(
  `data:text/javascript;base64,${Buffer.from(bundle.outputFiles[0].text).toString("base64")}`
);
after(() => stop());

const product = {
  slug: "foreverpin",
  name: "ForeverPin",
  description: "Styled QR codes.",
  id: "p-test",
  workspaceId: "w-ventures",
  status: "active",
  inactiveServices: [],
  repository: { name: "owner/forever-pin", url: "https://github.com/owner/forever-pin", defaultBranch: "main" },
  release: { asset: "foreverpin-release.tar.gz", workflow: null, images: [{ service: "api", image: "ghcr.io/owner/api" }] },
  homepage: null,
  identity: { source: "record", commit: null, problem: null, services: [] },
  iconUrl: "/api/products/foreverpin/icon",
  environments: [],
};

test("a product round-trips through its form, and a product without releases sends no release source", () => {
  const values = forms.productFormOf(product);
  assert.deepEqual(forms.productRequestOf(values, false), {
    name: "ForeverPin",
    description: "Styled QR codes.",
    repository: "owner/forever-pin",
    defaultBranch: "main",
    deploy: "wheelhouse",
    release: { asset: "foreverpin-release.tar.gz", workflow: null, images: [{ service: "api", image: "ghcr.io/owner/api" }] },
  });
  assert.equal(forms.productRequestOf({ ...values, publishes: false }, true).release, null);
  assert.equal(forms.productRequestOf(values, true).slug, "foreverpin");
  values.images[0].service = "edited";
  assert.equal(product.release.images[0].service, "api", "the form edits a detached copy");
});

test("what deploys a product round-trips through its form, a new one starts with Wheelhouse and an unknown mode is refused", () => {
  assert.equal(forms.productFormOf(null).deploy, "wheelhouse");
  const ci = forms.productFormOf({ ...product, deploy: "ci" });
  assert.equal(ci.deploy, "ci");
  assert.equal(forms.productRequestOf(ci, false).deploy, "ci");
  assert.equal(forms.productRequestOf({ ...ci, deploy: "none" }, true).deploy, "none");
  assert.equal(forms.productFormSchema.safeParse({ ...ci, deploy: "sometimes" }).success, false);
});

test("a product's workspace goes with its creation, and a saved product's moves and status changes follow", () => {
  const blank = forms.productFormOf(null, "w-platform");
  assert.equal(blank.workspaceId, "w-platform");
  assert.equal(blank.active, true);
  assert.equal(forms.productRequestOf({ ...blank, slug: "logs" }, true).workspaceId, "w-platform");
  assert.equal("workspaceId" in forms.productRequestOf(forms.productFormOf(null), true), false);
  assert.equal("workspaceId" in forms.productRequestOf(forms.productFormOf(product), false), false);
  const values = forms.productFormOf(product);
  assert.deepEqual(forms.productFollowUpsOf(values, product), { workspaceId: null, status: null });
  assert.deepEqual(forms.productFollowUpsOf({ ...values, workspaceId: "w-platform", active: false }, product),
    { workspaceId: "w-platform", status: "inactive" });
  assert.deepEqual(forms.productFollowUpsOf({ ...values, workspaceId: null }, product), { workspaceId: null, status: null });
});

test("a workspace form sends its slug only when new, and an emptied description clears it", () => {
  const workspace = { id: "w-platform", slug: "platform", name: "Platform", description: "Shared apps.", order: 0 };
  const values = forms.workspaceFormOf(workspace);
  assert.deepEqual(forms.updateWorkspaceRequestOf({ ...values, description: "" }), { name: "Platform", description: "" });
  assert.deepEqual(forms.createWorkspaceRequestOf({ slug: "labs", name: "Labs", description: "" }), { slug: "labs", name: "Labs" });
  assert.equal(forms.workspaceFormSchema.safeParse({ slug: "Labs!", name: "Labs", description: "" }).success, false);
  assert.equal(forms.workspaceFormSchema.safeParse({ slug: "labs", name: " ", description: "" }).success, false);
});

test("the product schema trims, refuses a bad slug and asks for an asset only while publishing", () => {
  const parsed = forms.productFormSchema.safeParse({ ...forms.productFormOf(product), name: "  ForeverPin  " });
  assert.equal(parsed.success, true);
  assert.equal(parsed.data.name, "ForeverPin");
  const refused = forms.productFormSchema.safeParse({ ...forms.productFormOf(product), slug: "Forever Pin", asset: "" });
  assert.deepEqual(refused.error.issues.map((issue) => issue.path.join(".")).sort(), ["asset", "slug"]);
  assert.equal(forms.productFormSchema.safeParse({ ...forms.productFormOf(product), publishes: false, asset: "" }).success, true);
});

test("host error paths land on the flat product and server fields", () => {
  assert.equal(forms.productFieldPath("Release.Images[0].Image"), "images[0].image");
  assert.equal(forms.productFieldPath("Release.Asset"), "asset");
  assert.equal(forms.productFieldPath("DefaultBranch"), "defaultBranch");
  assert.equal(forms.serverFieldPath("Ingress.EntryPoints[1]"), "entryPoints");
  assert.equal(forms.serverFieldPath("Ingress.PrivateProbe"), "privateProbe");
  assert.equal(forms.serverFieldPath("SshPort"), "sshPort");
});

test("a server's empty optional ingress text is sent as absent and a new server defaults to Hetzner on 22", () => {
  const values = forms.serverFormOf(null);
  assert.equal(values.provider, "hetzner");
  assert.equal(values.sshPort, 22);
  const body = forms.serverRequestOf({ ...values, slug: "hel1", name: "Helsinki", host: "vps.example.net", region: "hel1" }, true);
  assert.equal(body.slug, "hel1");
  assert.deepEqual(body.ingress, {
    scheme: "https", port: null, entryPoints: ["websecure"], privateEntryPoints: [],
    certResolver: "letsencrypt", pattern: null, probe: null, privateProbe: null,
  });
  assert.equal(forms.serverFormSchema.safeParse({ ...values, sshPort: null }).success, false);
});

test("a new environment starts as dev on the first server, and its slug follows the environment", () => {
  const values = forms.targetFormOf(null, "foreverpin", "hel1");
  assert.equal(values.slug, "foreverpin-dev");
  assert.equal(values.server, "hel1");
  assert.equal(forms.suggestedTargetSlug("foreverpin", "prod"), "foreverpin-prod");
  const body = forms.targetRequestOf({ ...values, sites: [{ site: "app", host: "dev.example.com" }] }, "foreverpin", false);
  assert.equal("slug" in body, false);
  assert.equal(body.product, "foreverpin");
  assert.deepEqual(body.sites, [{ site: "app", host: "dev.example.com" }]);
  assert.equal(forms.targetFormSchema.safeParse({ ...values, server: "" }).success, false);
});

test("a vault keeps its server, and a new one is named after it", () => {
  const values = forms.vaultDefinitionFormOf(null, "hel1");
  assert.equal(values.slug, "hel1-vault");
  assert.deepEqual(forms.vaultDefinitionRequestOf({ ...values, name: "Vault" }, "hel1", false), {
    name: "Vault", server: "hel1", url: "http://vault:8080",
  });
});
