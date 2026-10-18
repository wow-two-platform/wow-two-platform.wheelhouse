import assert from "node:assert/strict";
import { after, afterEach, test } from "node:test";
import { stop } from "esbuild";
import { bundleModule } from "./BundleModule.mjs";

const { CatalogRules, catalogApi, workspacesApi, pinsApi } = await bundleModule(`
  export { CatalogRules } from './src/domain/catalog/CatalogRules';
  export { catalogApi } from './src/integration/catalog';
  export { workspacesApi } from './src/integration/workspaces';
  export { pinsApi } from './src/integration/pins';
`);
const originalFetch = globalThis.fetch;
afterEach(() => {
  globalThis.fetch = originalFetch;
});
after(() => stop());

const json = (data, status = 200) =>
  new Response(JSON.stringify(data), { status, headers: { "Content-Type": "application/json" } });
const entry = (slug, fields = {}) => ({ slug, name: slug, workspace: "ventures", kind: "app", deploy: "wheelhouse",
  purpose: "", repositories: [], productId: null, ...fields });
const entries = [
  entry("hijinx"),
  entry("wheelhouse", { productId: "p-wh" }),
  entry("sdk-fe", { deploy: "npm" }),
  entry("museums"),
];

test("only entries Wheelhouse deploys and has not imported are importable", () => {
  assert.deepEqual(CatalogRules.importable(entries), ["hijinx", "museums"]);
  assert.deepEqual(entries.map(CatalogRules.stateOf), ["new", "in-wheelhouse", "external", "new"]);
  assert.equal(CatalogRules.isImportable(entry("x", { deploy: "Wheelhouse" })), false);
});

test("the selection keeps importable slugs once, in catalog order, and drops the rest", () => {
  assert.deepEqual(CatalogRules.selection(entries, ["museums", "wheelhouse", "hijinx", "museums", 7, "gone"]),
    ["hijinx", "museums"]);
  assert.deepEqual(CatalogRules.toggle(entries, ["museums"], "hijinx"), ["hijinx", "museums"]);
  assert.deepEqual(CatalogRules.toggle(entries, ["hijinx", "museums"], "hijinx"), ["museums"]);
  assert.deepEqual(CatalogRules.toggle(entries, ["museums"], "wheelhouse"), ["museums"]);
  const afterImport = entries.map((item) => (item.slug === "hijinx" ? { ...item, productId: "p-hx" } : item));
  assert.deepEqual(CatalogRules.selection(afterImport, ["hijinx", "museums"]), ["museums"]);
});

test("apps group by workspace in catalog order, an unlisted workspace last, each with its counts", () => {
  const catalog = {
    workspaces: [
      { slug: "platform", name: "Platform", description: "Control plane" },
      { slug: "ventures", name: "Ventures", description: "" },
      { slug: "empty", name: "Empty", description: "" },
    ],
    entries: [
      ...entries,
      entry("logs", { workspace: "platform" }),
      entry("vault", { workspace: "platform", productId: "p-vault" }),
      entry("stray", { workspace: "lost" }),
    ],
  };
  const groups = CatalogRules.groups(catalog);
  assert.deepEqual(groups.map((group) => group.slug), ["platform", "ventures", "lost"]);
  assert.deepEqual(groups.map((group) => group.name), ["Platform", "Ventures", "lost"]);
  const [platform, ventures] = groups;
  assert.deepEqual(platform.importable, ["logs"]);
  assert.deepEqual(ventures.importable, ["hijinx", "museums"]);
  assert.deepEqual([ventures.inWheelhouse, ventures.notDeployable], [1, 1]);
  assert.equal(CatalogRules.countsLine(ventures), "2 importable · 1 in Wheelhouse · 1 not deployable");
  assert.equal(CatalogRules.countsLine(platform), "1 importable · 1 in Wheelhouse");
  assert.equal(CatalogRules.countsLine({ importable: [], inWheelhouse: 2, notDeployable: 0 }), "Nothing to import · 2 in Wheelhouse");
});

test("a workspace's importable apps are selected and cleared together, leaving other workspaces' choices alone", () => {
  const all = [...entries, entry("logs", { workspace: "platform" })];
  const [ventures, platform] = CatalogRules.groups({ workspaces: [], entries: all });
  assert.equal(CatalogRules.workspaceSelection(ventures, []), "none");
  assert.equal(CatalogRules.workspaceSelection(ventures, ["museums"]), "some");
  const chosen = CatalogRules.selectWorkspace(all, ["logs"], ventures, true);
  assert.deepEqual(chosen, ["hijinx", "museums", "logs"]);
  assert.equal(CatalogRules.workspaceSelection(ventures, chosen), "all");
  assert.deepEqual(CatalogRules.selectWorkspace(all, chosen, ventures, false), ["logs"]);
  assert.equal(CatalogRules.workspaceSelection(platform, ["logs"]), "all");
  assert.equal(CatalogRules.workspaceSelection({ importable: [] }, ["logs"]), "none");
});

test("the import result and the source read as one line each", () => {
  assert.equal(CatalogRules.summary({ createdWorkspaces: 1, createdProducts: 2, movedProducts: 1 }),
    "Added 2 products, created 1 workspace and moved 1 product.");
  assert.equal(CatalogRules.summary({ createdWorkspaces: 0, createdProducts: 1, movedProducts: 0 }), "Added 1 product.");
  assert.match(CatalogRules.summary({ createdWorkspaces: 0, createdProducts: 0, movedProducts: 0 }), /^Nothing to import/);
  assert.equal(CatalogRules.sourceLabel({ repository: "wow-two/wow-two-ws", path: "catalog.yml", ref: "main",
    commit: "4f2c9e1a7b3d" }), "wow-two/wow-two-ws@4f2c9e1");
  assert.equal(CatalogRules.sourceLabel({ repository: "wow-two/wow-two-ws", path: "catalog.yml", ref: "main", commit: null }),
    "wow-two/wow-two-ws@main");
  const warnings = [1, 2, 3].map((index) => ({ slug: `app-${index}`, message: "created without images" }));
  assert.equal(CatalogRules.warningLine({ warnings }),
    "app-1: created without images; app-2: created without images; and 1 more");
  assert.equal(CatalogRules.warningLine({ warnings: [] }), "");
});

test("the catalog decodes, and an import posts its slugs with the catalog action and reads counts or lists", async () => {
  const requests = [];
  globalThis.fetch = async (url, options) => {
    requests.push({ url, options });
    if (url === "/api/catalog")
      return json({ data: { source: { repository: "o/r", path: "catalog.yml", ref: "main", commit: null },
        workspaces: [{ slug: "ventures", name: "Ventures", description: null }], entries } });
    return json({ data: { createdWorkspaces: ["labs"], createdProducts: 2, movedProducts: [] } });
  };
  const catalog = await catalogApi.getCatalog();
  assert.equal(catalog.ok, true);
  assert.equal(catalog.value.workspaces[0].description, "");
  const result = await catalogApi.importEntries(["hijinx", "museums"]);
  assert.deepEqual(result.value, { createdWorkspaces: 1, createdProducts: 2, movedProducts: 0, warnings: [] });
  assert.equal(requests[1].url, "/api/catalog/import");
  assert.equal(requests[1].options.method, "POST");
  assert.equal(requests[1].options.headers.get("X-Wheelhouse-Action"), "catalog");
  assert.deepEqual(JSON.parse(requests[1].options.body), { slugs: ["hijinx", "museums"] });
});

test("workspace and pin writes carry their actions; a reorder sends every id and any success counts", async () => {
  const requests = [];
  globalThis.fetch = async (url, options) => {
    requests.push([url, options.method, options.headers.get("X-Wheelhouse-Action"), options.body ? JSON.parse(options.body) : null]);
    return options.method === "DELETE" ? new Response(null, { status: 204 }) : json({ data: [] });
  };
  assert.equal((await workspacesApi.reorderWorkspaces(["w-b", "w-a"])).ok, true);
  assert.equal((await pinsApi.createPin({ kind: "product", productId: "p-wh" })).ok, true);
  assert.equal((await pinsApi.reorderPins(["pin-2", "pin-1"])).ok, true);
  assert.equal((await pinsApi.deletePin("pin-1")).ok, true);
  assert.deepEqual(requests, [
    ["/api/workspaces/order", "PUT", "workspace", { ids: ["w-b", "w-a"] }],
    ["/api/pins", "POST", "pin", { kind: "product", productId: "p-wh" }],
    ["/api/pins/order", "PUT", "pin", { ids: ["pin-2", "pin-1"] }],
    ["/api/pins/pin-1", "DELETE", "pin", null],
  ]);
});

test("workspaces and pins decode absent optional fields as empty", async () => {
  globalThis.fetch = async (url) =>
    url === "/api/workspaces"
      ? json({ data: [{ id: "w-a", slug: "a", name: "A", description: null, order: 0 }] })
      : json({ data: [{ id: "pin-1", kind: "workspace", workspaceId: "w-a", order: 0 }] });
  assert.equal((await workspacesApi.listWorkspaces()).value[0].description, "");
  assert.deepEqual((await pinsApi.listPins()).value[0], { id: "pin-1", kind: "workspace", workspaceId: "w-a", productId: null,
    service: null, order: 0 });
  globalThis.fetch = async () => json({ data: [{ id: "pin-1", kind: "folder", order: 0 }] });
  assert.equal((await pinsApi.listPins()).failure.code, "protocol");
});
