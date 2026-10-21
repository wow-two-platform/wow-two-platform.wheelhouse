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
const entry = (slug, fields = {}) => ({ slug, name: slug, source: "o/ventures", workspace: "ventures", kind: "app",
  deploy: "wheelhouse", purpose: "", repositories: [], productId: null, ...fields });
const hub = (repository, fields = {}) => ({ repository, path: "catalog.yml", ref: "main", name: null, commit: null,
  error: null, ...fields });
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

test("the catalog groups hub by hub in the read order, each with its workspaces and counts, a failed hub empty", () => {
  const catalog = {
    sources: [
      hub("wow-two/wow-two-ws", { name: "wow2", commit: "4f2c9e1a7b3d" }),
      hub("o/ventures", { name: "10x Ventures", commit: "aa11bb22cc33" }),
      hub("ocharo-hq/ocharo-ws", { commit: "ff00", error: "catalog.yml in ocharo-hq/ocharo-ws@main, line 3: …" }),
    ],
    workspaces: [
      { slug: "wow2-platform", name: "wow2 platform", description: "", source: "wow-two/wow-two-ws" },
      { slug: "ventures", name: "Ventures", description: "", source: "o/ventures" },
    ],
    entries: [...entries, entry("logs", { workspace: "wow2-platform", source: "wow-two/wow-two-ws" }), entry("lost", { source: "x/gone" })],
  };
  const hubs = CatalogRules.sources(catalog);
  assert.deepEqual(hubs.map((group) => group.name), ["wow2", "10x Ventures", "ocharo-hq/ocharo-ws", "x/gone"]);
  assert.deepEqual(hubs.map((group) => group.groups.map((workspace) => workspace.slug)),
    [["wow2-platform"], ["ventures"], [], ["ventures"]]);
  assert.deepEqual(hubs.map((group) => [group.entries, group.importable]), [[1, 1], [4, 2], [0, 0], [1, 1]]);
  assert.equal(CatalogRules.sourceCountsLine(hubs[1]), "4 apps · 2 importable");
  assert.equal(CatalogRules.sourceCountsLine({ ...hubs[0], importable: 0 }), "1 app · nothing to import");
  assert.equal(CatalogRules.sourceCountsLine(hubs[2]), "Not listed");
  assert.equal(CatalogRules.sourceHref(hubs[0].source), "https://github.com/wow-two/wow-two-ws/blob/4f2c9e1a7b3d/catalog.yml");
  assert.equal(CatalogRules.sourceHref(hub("o/r")), "https://github.com/o/r/blob/main/catalog.yml");
  assert.equal(CatalogRules.sourceHref(hubs[3].source), null);
  assert.equal(CatalogRules.sourceHref(hub("not a repository")), null);
});

test("the import result and the source read as one line each", () => {
  assert.equal(CatalogRules.summary({ createdWorkspaces: 1, createdProducts: 2, movedProducts: 1 }),
    "Added 2 products, created 1 workspace and moved 1 product.");
  assert.equal(CatalogRules.summary({ createdWorkspaces: 0, createdProducts: 1, movedProducts: 0 }), "Added 1 product.");
  assert.match(CatalogRules.summary({ createdWorkspaces: 0, createdProducts: 0, movedProducts: 0, updatedProducts: 0 }), /^Nothing to import/);
  assert.equal(CatalogRules.summary({ createdWorkspaces: 0, createdProducts: 0, movedProducts: 1, updatedProducts: 1 }),
    "Moved 1 product and set how 1 product deploys.");
  assert.equal(CatalogRules.summary({ createdWorkspaces: 0, createdProducts: 0, movedProducts: 0, updatedProducts: 2 }),
    "Set how 2 products deploy.");
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
      return json({ data: {
        sources: [{ repository: "o/ventures", path: "catalog.yml", ref: "main", commit: null, error: "o/ventures@main has no catalog.yml." },
          { repository: "o/r", path: "catalog.yml", ref: "main", name: "R", commit: "abc" }],
        workspaces: [{ slug: "ventures", name: "Ventures", description: null, source: "o/r" }], entries } });
    return json({ data: { createdWorkspaces: ["labs"], createdProducts: 2, movedProducts: [] } });
  };
  const catalog = await catalogApi.getCatalog();
  assert.equal(catalog.ok, true);
  assert.equal(catalog.value.workspaces[0].description, "");
  assert.deepEqual(catalog.value.sources.map((source) => [source.name, source.commit, source.error]),
    [[null, null, "o/ventures@main has no catalog.yml."], ["R", "abc", null]]);
  assert.equal(catalog.value.entries[0].source, "o/ventures");
  const result = await catalogApi.importEntries(["hijinx", "museums"]);
  assert.deepEqual(result.value,
    { createdWorkspaces: 1, createdProducts: 2, movedProducts: 0, updatedProducts: 0, retiredProducts: 0, warnings: [] },
    "a host that reports no deploy-mode changes or retirements counts none");
  assert.deepEqual(catalog.value.entries[0].replaces, [], "an entry without replaces supersedes nothing");
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

test("an entry of several repositories lists them, retires what it replaces, and the summary says so", async () => {
  globalThis.fetch = async (url) => url === "/api/catalog"
    ? json({ data: { sources: [], workspaces: [], entries: [entry("ocharo", {
      repositories: ["ocharo-hq/ocharo-platform", "ocharo-hq/ocharo-studio"], replaces: ["ocharo-platform", "ocharo-studio"] })] } })
    : json({ data: { createdWorkspaces: [], createdProducts: ["ocharo"], movedProducts: [], updatedProducts: [],
      retiredProducts: ["ocharo-platform", "ocharo-studio"], warnings: [] } });
  const catalog = await catalogApi.getCatalog();
  assert.deepEqual(catalog.value.entries[0].replaces, ["ocharo-platform", "ocharo-studio"]);
  assert.equal(catalog.value.entries[0].repositories.length, 2);
  const result = await catalogApi.importEntries(["ocharo"]);
  assert.equal(result.value.retiredProducts, 2);
  assert.equal(CatalogRules.summary(result.value), "Added 1 product and retired 2 replaced products.");
});
