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
      export { VpsProvider, VpsProviderLabels } from './src/domain/servers';
      export { BaselineHost } from './src/domain/targets';
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

test("the server form takes every provider, each with its own label", () => {
  const values = { ...forms.serverFormOf(null), slug: "vps1", name: "VPS", host: "vps.example.net", region: "eu" };
  for (const [provider, label] of [["ovhcloud", "OVHcloud"], ["contabo", "Contabo"]]) {
    assert.equal(forms.serverFormSchema.safeParse({ ...values, provider }).success, true, provider);
    assert.equal(forms.VpsProviderLabels[provider], label);
  }
  assert.equal(forms.serverFormSchema.safeParse({ ...values, provider: "vultr" }).success, false);
  assert.deepEqual(Object.keys(forms.VpsProviderLabels).sort(), Object.values(forms.VpsProvider).sort());
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

test("a public site's baseline host follows the product, site, environment and server address (decision 68)", () => {
  const { BaselineHost } = forms;
  assert.equal(BaselineHost.of("ocharo-studio", "app", "dev", "203.0.113.10", true), "ocharo-studio-dev.203-0-113-10.sslip.io");
  assert.equal(BaselineHost.of("ocharo-studio", "app", "prod", "203.0.113.10", true), "ocharo-studio.203-0-113-10.sslip.io");
  // A second site adds its name after the product.
  assert.equal(BaselineHost.of("ocharo-studio", "admin", "dev", "203.0.113.10", false), "ocharo-studio-admin-dev.203-0-113-10.sslip.io");
  assert.equal(BaselineHost.of("ocharo-studio", "app", "dev", null, true), null);
  assert.equal(BaselineHost.of("ocharo-studio", "app", "dev", "2001:db8::1", true), null);
  assert.equal(BaselineHost.of("p".repeat(48), "s".repeat(20), "dev", "203.0.113.10", false), null, "a label over 63 characters");
  assert.equal(BaselineHost.isBaseline("ocharo-studio-dev.203-0-113-10.sslip.io"), true);
  assert.equal(BaselineHost.isBaseline("studio-dev.ocharo.co"), false);
});

test("the environment form knows a product's sites from its rollouts and the public address of its server", () => {
  const known = forms.knownSitesOf({
    environments: [
      { name: "dev", sites: [{ name: "app", url: "https://a", exposure: "public", primary: true },
        { name: "admin", url: "https://b", exposure: "private", primary: false }] },
      { name: "prod", sites: [{ name: "status", url: "https://c", exposure: "public", primary: false }] },
    ],
  });
  assert.deepEqual(known, [
    { name: "admin", exposure: "private", primary: false },
    { name: "app", exposure: "public", primary: true },
    { name: "status", exposure: "public", primary: false },
  ]);
  assert.equal(forms.publicAddressOf([{ slug: "ocharo-vps", ingress: { publicAddress: "203.0.113.10" } }], "ocharo-vps"), "203.0.113.10");
  assert.equal(forms.publicAddressOf([{ slug: "old", ingress: { publicAddress: null } }], "old"), null);
  // Before a rollout marked a front door, the first site by name is it.
  assert.deepEqual(forms.knownSitesOf({ environments: [{ name: "dev", sites: [
    { name: "go", url: "https://g", exposure: "public" }, { name: "app", url: "https://a", exposure: "public" }] }] })
    .map((site) => [site.name, site.primary]), [["app", true], ["go", false]]);
  assert.deepEqual(forms.knownSitesOf(null), []);
});

test("the environment form knows a product's sites from its deploy.yml before any rollout", () => {
  const descriptor = { commit: "a".repeat(40), problem: null, services: [{ service: "studio", needs: [] }],
    sites: [{ name: "app", exposure: "public", primary: true }, { name: "admin", exposure: "private", primary: false }] };
  const known = forms.knownSitesOf({ environments: [] }, descriptor);
  assert.deepEqual(known, [{ name: "admin", exposure: "private", primary: false }, { name: "app", exposure: "public", primary: true }]);
  // deploy.yml wins over what an older rollout published; one that cannot be read leaves the rollouts as the source.
  const rolled = { environments: [{ name: "dev", sites: [{ name: "old", url: "https://o", exposure: "public", primary: true }] }] };
  assert.deepEqual(forms.knownSitesOf(rolled, descriptor).map((site) => site.name), ["admin", "app"]);
  assert.deepEqual(forms.knownSitesOf(rolled, { ...descriptor, problem: "no deploy.yml", sites: [] }).map((site) => site.name), ["old"]);
});

const ocharo = [
  { name: "desk", exposure: "public", primary: true },
  { name: "motion", exposure: "private", primary: false },
  { name: "studio", exposure: "public", primary: false },
];
const rowContext = (environment, address = "203.0.113.10") => ({ product: "ocharo", environment, address,
  node: (site) => forms.tailnetNodeOf("ocharo", site, "ocharo-vps", environment) });

test("every site the product declares is a row: the derived baseline host, or the tailnet node of a private site", () => {
  const dev = forms.siteRowsOf(ocharo, [], rowContext("dev"));
  assert.deepEqual(dev, [
    { site: "desk", exposure: "public", host: "ocharo-dev.203-0-113-10.sslip.io", node: null, note: null },
    { site: "motion", exposure: "private", host: "", node: "ocharo-motion-dev", note: "Private; tailnet node" },
    { site: "studio", exposure: "public", host: "ocharo-studio-dev.203-0-113-10.sslip.io", node: null, note: null },
  ]);
  // Only the hosts are sent: a private site's node is no host.
  assert.deepEqual(forms.siteHostsOf(dev), [
    { site: "desk", host: "ocharo-dev.203-0-113-10.sslip.io" },
    { site: "studio", host: "ocharo-studio-dev.203-0-113-10.sslip.io" },
  ]);
  const prod = forms.siteRowsOf(ocharo, [], rowContext("prod"));
  assert.deepEqual(prod.map((row) => row.host || row.node),
    ["ocharo.203-0-113-10.sslip.io", "ocharo-motion", "ocharo-studio.203-0-113-10.sslip.io"]);
  // Without a public address a public site has no host to show or send; the deploy fills it.
  const unplaced = forms.siteRowsOf(ocharo, [], rowContext("dev", null));
  assert.deepEqual(unplaced[0], { site: "desk", exposure: "public", host: "", node: null, note: "Set once the server has a public address" });
  assert.deepEqual(forms.siteHostsOf(unplaced), []);
  assert.deepEqual(forms.siteRowsOf([], [], rowContext("dev")), []);
});

test("a site's private node is the one its server registered, else the product and site, with -dev in dev", () => {
  const placements = [{ serverId: "ocharo-vps", facts: { databases: [], sites: [
    { site: "motion", environment: "prod", node: "ocharo-motion-lab", address: "x", joined: true }] } }];
  assert.equal(forms.tailnetNodeOf("ocharo", "motion", "ocharo-vps", "prod", placements), "ocharo-motion-lab");
  assert.equal(forms.tailnetNodeOf("ocharo", "motion", "ocharo-vps", "dev", placements), "ocharo-motion-lab-dev");
  assert.equal(forms.tailnetNodeOf("ocharo", "motion", "other-vps", "dev", placements), "ocharo-motion-dev");
});

test("a target keeps the hosts it stores, notes the one that is not the derived host and drops none unseen", () => {
  const stored = [
    { site: "desk", host: "ocharo-dev.203-0-113-10.sslip.io" },
    { site: "studio", host: "studio-dev.ocharo.example" },
    { site: "legacy", host: "legacy.ocharo.example" },
  ];
  const rows = forms.siteRowsOf(ocharo, stored, rowContext("dev"));
  assert.deepEqual(rows.map((row) => [row.site, row.host, row.note]), [
    ["desk", "ocharo-dev.203-0-113-10.sslip.io", null],
    ["motion", "", "Private; tailnet node"],
    ["studio", "studio-dev.ocharo.example", "Stored; derived: ocharo-studio-dev.203-0-113-10.sslip.io"],
    ["legacy", "legacy.ocharo.example", "Stored; not declared by the product"],
  ]);
  assert.equal(rows[3].exposure, null);
  assert.deepEqual(forms.siteHostsOf(rows), [stored[0], stored[1], stored[2]]);
  // A private site with a host of its own keeps it, still labelled private.
  const bound = forms.siteRowsOf(ocharo, [{ site: "motion", host: "motion.ocharo.example" }], rowContext("dev"));
  assert.deepEqual([bound[1].host, bound[1].node, bound[1].note], ["motion.ocharo.example", "ocharo-motion-dev", "Private; stored host"]);
  // A stored host with no address to derive from is kept, noted plainly.
  assert.equal(forms.siteRowsOf(ocharo, stored, rowContext("dev", null))[0].note, "Stored");
});

test("moving a target to another environment or server re-derives its baseline hosts and keeps a bound one", () => {
  const target = { environment: "dev", server: "ocharo-vps", sites: [
    { site: "desk", host: "ocharo-dev.203-0-113-10.sslip.io" },
    { site: "studio", host: "studio-dev.ocharo.example" },
  ] };
  assert.deepEqual(forms.storedHostsOf(target, "dev", "ocharo-vps"), target.sites);
  assert.deepEqual(forms.storedHostsOf(target, "prod", "ocharo-vps"), [target.sites[1]]);
  assert.deepEqual(forms.storedHostsOf(target, "dev", "other-vps"), [target.sites[1]]);
  assert.deepEqual(forms.storedHostsOf(null, "dev", "ocharo-vps"), []);
  assert.deepEqual(forms.storedHostsOf({ ...target, sites: [{ site: "desk", host: " " }] }, "dev", "ocharo-vps"), []);
});

test("Wheelhouse's own environment runs on the control host and takes what this console derives about itself", () => {
  const servers = [{ slug: "pilot", controlHost: false }, { slug: "control", controlHost: true }, { slug: "old" }];
  assert.deepEqual(forms.serverChoicesOf("wheelhouse", servers).map((server) => server.slug), ["control"]);
  assert.deepEqual(forms.serverChoicesOf("foreverpin", servers).map((server) => server.slug), ["pilot", "control", "old"]);

  const host = "wheelhouse.tail1234.ts.net";
  const derived = (environment, sites = [{ site: "console", host }]) => ({
    environment, network: "control", root: "/srv/wheelhouse", sites,
    settings: [{ service: "console", path: `/srv/wheelhouse/config/console-${environment}.json` }],
  });
  const self = { product: "wheelhouse", host, environments: [derived("dev"), derived("prod")] };
  const dev = forms.selfEnvironmentOf(self, "dev");
  const blank = forms.targetFormOf(null, "wheelhouse", "control", "dev");
  assert.equal(blank.network, "platform", "a product's environment starts on the products' network");
  const values = forms.withSelfDefinition({ ...blank, smokeChecks: [{ service: "console", path: "/health", status: 200 }] }, dev);
  assert.deepEqual([values.network, values.root, values.settings, values.smokeChecks.length],
    ["control", "/srv/wheelhouse", [{ service: "console", path: "/srv/wheelhouse/config/console-dev.json" }], 1]);
  values.settings[0].path = "/edited";
  assert.equal(dev.settings[0].path, "/srv/wheelhouse/config/console-dev.json", "the form edits a detached copy");

  const rows = forms.selfSiteRowsOf(dev, "dev");
  assert.deepEqual(forms.siteHostsOf(rows), [{ site: "console", host }]);
  assert.equal(rows[0].note, "This console's hostname; dev on port 8443");
  assert.equal(forms.selfSiteRowsOf(forms.selfEnvironmentOf(self, "prod"), "prod")[0].note, "This console's hostname");
  const unnamed = forms.selfSiteRowsOf(derived("dev", []), "dev");
  assert.deepEqual([unnamed[0].site, unnamed[0].host, unnamed[0].note], ["console", "", "This console's settings name no hostname"]);
  assert.deepEqual(forms.siteHostsOf(unnamed), []);
  assert.deepEqual(forms.selfSiteRowsOf(null, "dev"), [], "no row before the console's read arrives");
  assert.equal(forms.selfEnvironmentOf(null, "dev"), null);
});

test("a new environment is the first one on the promotion path the product lacks", () => {
  assert.equal(forms.firstFreeEnvironment([]), "dev");
  assert.equal(forms.firstFreeEnvironment(["dev"]), "prod");
  assert.equal(forms.firstFreeEnvironment(["prod"]), "dev");
  assert.equal(forms.firstFreeEnvironment(["dev", "prod"]), "dev");
  const values = forms.targetFormOf(null, "ocharo", "hel1", "prod");
  assert.equal(values.environment, "prod");
  assert.equal(values.slug, "ocharo-prod");
  assert.equal(forms.targetFormOf({ slug: "ocharo-dev", product: "ocharo", server: "hel1", environment: "dev", network: "platform",
    root: "/srv/wheelhouse", settings: [], smokeChecks: [], sites: [] }, "ocharo", "x", "prod").environment, "dev");
});

test("a server edit never sends the public address enrollment recorded", () => {
  const server = { slug: "ocharo-vps", name: "Ocharo VPS", provider: "contabo", host: "100.101.7.8", region: "eu",
    sshUser: "wheelhouse-deploy", sshPort: 22, ingress: { scheme: "https", port: null, entryPoints: ["websecure"],
      privateEntryPoints: ["private"], certResolver: "letsencrypt", pattern: null, probe: "https://127.0.0.1",
      privateProbe: "http://127.0.0.1:18080", publicAddress: "203.0.113.10" } };
  const body = forms.serverRequestOf(forms.serverFormOf(server), false);
  assert.equal("publicAddress" in body.ingress, false);
  assert.deepEqual(body.ingress.entryPoints, ["websecure"]);
});

test("a vault keeps its server, and a new one is named after it", () => {
  const values = forms.vaultDefinitionFormOf(null, "hel1");
  assert.equal(values.slug, "hel1-vault");
  assert.deepEqual(forms.vaultDefinitionRequestOf({ ...values, name: "Vault" }, "hel1", false), {
    name: "Vault", server: "hel1", url: "http://vault:8080",
  });
});
