import assert from "node:assert/strict";
import { after, afterEach, test } from "node:test";
import { resolve } from "node:path";
import { build, stop } from "esbuild";

// Bundle the real identity modules in memory: the pure rules, the API client, the product form and the products
// composable. No app server, browser or live API is needed.
const bundle = await build({
  stdin: {
    contents: `
      export * from './src/domain/products';
      export { productsApi } from './src/integration/products';
      export { failureReason } from './src/integration/common';
      export { productFormOf, productFormSchema, productRequestOf } from './src/application/products/ProductForms';
      export { useProducts, ProductKeys } from './src/application/products';
      export { queryClient, queryPlugin } from './src/bootstrap/query';
      export { createRenderer, nextTick } from 'vue';
    `,
    resolveDir: process.cwd(),
  },
  alias: { "@": resolve("src") },
  bundle: true,
  platform: "node",
  format: "esm",
  write: false,
});
const identity = await import(
  `data:text/javascript;base64,${Buffer.from(bundle.outputFiles[0].text).toString("base64")}`
);
const originalFetch = globalThis.fetch;
afterEach(() => {
  globalThis.fetch = originalFetch;
  identity.queryClient.clear();
});
after(() => stop());

const commit = "0123456789abcdef0123456789abcdef01234567";

function json(data, status = 200, headers = {}) {
  return new Response(JSON.stringify(data), {
    status,
    headers: { "Content-Type": "application/json", ...headers },
  });
}

const record = {
  slug: "foreverpin",
  name: "ForeverPin",
  description: "Styled QR codes.",
  homepage: null,
  identity: { source: "record", commit: null, problem: null, services: [] },
  id: "p-test",
  workspaceId: "w-ventures",
  status: "active",
  inactiveServices: [],
  repository: { name: "owner/forever-pin", url: "https://github.com/owner/forever-pin", defaultBranch: "main" },
  release: null,
  iconUrl: "/api/products/foreverpin/icon",
  environments: [],
};

const fromManifest = {
  ...record,
  name: "Forever Pin",
  description: "QR codes that outlive the print.",
  homepage: "https://foreverpin.example/start?ref=console",
  identity: {
    source: "manifest",
    commit,
    problem: null,
    services: [
      { service: "redirect", name: null, description: "Answers a scan." },
      { service: "management", name: "Editor", description: null },
    ],
  },
};

test("identity fields are read-only exactly while the product's manifest is their source", () => {
  assert.equal(identity.isIdentityReadOnly(fromManifest), true);
  assert.equal(identity.isIdentityReadOnly(record), false);
  assert.equal(identity.isIdentityReadOnly(null), false, "a product being created is edited in the console");
  assert.equal(identity.isIdentityReadOnly(undefined), false);
  assert.equal(
    identity.isIdentityReadOnly({ ...record, identity: { ...record.identity, problem: "'name' is required." } }),
    false,
    "an ignored first manifest leaves the row as the source",
  );
});

test("the note beside read-only fields names the manifest path, and editable fields carry none", () => {
  assert.equal(identity.ProductManifestPath, "product/product.json");
  assert.equal(identity.identityNoteOf(fromManifest), "Edited in product/product.json in the repository.");
  assert.equal(identity.identityNoteOf(record), null);
  assert.equal(identity.identityNoteOf(null), null);
});

test("an ignored manifest is explained, and says the last valid copy stays only while one applies", () => {
  const kept = { ...fromManifest, identity: { ...fromManifest.identity, problem: "'name' must hold 1–80 characters." } };
  assert.equal(
    identity.identityProblemOf(kept),
    "product/product.json is ignored: 'name' must hold 1–80 characters. The last valid copy stays.",
  );
  const first = { ...record, identity: { ...record.identity, problem: "The file is not valid JSON (line 3)." } };
  assert.equal(identity.identityProblemOf(first), "product/product.json is ignored: The file is not valid JSON (line 3).");
  assert.equal(identity.identityProblemOf(fromManifest), null);
  assert.equal(identity.identityProblemOf(record), null);
});

test("a homepage links out only over http or https, and shows its host", () => {
  assert.deepEqual(identity.homepageLinkOf(fromManifest), {
    href: "https://foreverpin.example/start?ref=console",
    host: "foreverpin.example",
  });
  assert.deepEqual(identity.homepageLinkOf({ ...record, homepage: "http://localhost:8080/" }), {
    href: "http://localhost:8080/",
    host: "localhost:8080",
  });
  assert.equal(identity.homepageLinkOf(record), null);
  assert.equal(identity.homepageLinkOf({ ...record, homepage: "javascript:alert(1)" }), null);
  assert.equal(identity.homepageLinkOf({ ...record, homepage: "data:text/html,<b>x</b>" }), null);
  assert.equal(identity.homepageLinkOf({ ...record, homepage: "not an address" }), null);
  assert.equal(identity.homepageLinkOf(null), null);
});

test("the commit shows as its first seven characters", () => {
  assert.equal(identity.shortCommitOf(commit), "0123456");
  assert.equal(identity.shortCommitOf(null), null);
  assert.equal(identity.shortCommitOf(""), null);
});

test("services list in key order, each by its manifest name or else its key", () => {
  assert.deepEqual(identity.serviceIdentitiesOf(fromManifest), [
    { service: "management", label: "Editor", description: null },
    { service: "redirect", label: "redirect", description: "Answers a scan." },
  ]);
  assert.deepEqual(identity.serviceIdentitiesOf(record), []);
  assert.deepEqual(identity.serviceIdentitiesOf(null), []);
  const original = fromManifest.identity.services.map((service) => service.service);
  assert.deepEqual(original, ["redirect", "management"], "listing never reorders the product");
});

test("the product form opens on the manifest's values and still sends them as the effective ones", () => {
  const values = identity.productFormOf(fromManifest);
  assert.equal(values.name, "Forever Pin");
  assert.equal(values.description, "QR codes that outlive the print.");
  assert.equal(identity.productFormSchema.safeParse(values).success, true);
  const body = identity.productRequestOf(values, false);
  assert.equal(body.name, "Forever Pin", "the host ignores it while the manifest governs");
  assert.equal("homepage" in body, false, "the homepage is never part of a save");
  assert.equal("identity" in body, false);
});

test("the API client decodes a manifest-sourced product with its commit and services", async () => {
  globalThis.fetch = async () => json({ data: [fromManifest, record] });
  const result = await identity.productsApi.listProducts();
  assert.equal(result.ok, true);
  assert.deepEqual(result.value[0].identity, fromManifest.identity);
  assert.equal(result.value[0].homepage, "https://foreverpin.example/start?ref=console");
  assert.equal(result.value[1].identity.source, "record");
});

test("a product without the identity block is refused as a protocol error", async () => {
  const { identity: _dropped, ...stale } = record;
  globalThis.fetch = async () => json({ data: [stale] });
  const result = await identity.productsApi.listProducts();
  assert.equal(result.ok, false);
  assert.equal(result.failure.code, "protocol");

  globalThis.fetch = async () => json({ data: [{ ...record, identity: { ...record.identity, source: "cache" } }] });
  assert.equal((await identity.productsApi.listProducts()).ok, false, "an unknown source is not guessed at");
});

test("refreshing posts once to the identity route with its action header and decodes the product", async () => {
  let captured;
  globalThis.fetch = async (url, options) => {
    captured = { url, options };
    return json({ data: fromManifest });
  };

  const result = await identity.productsApi.refreshIdentity("foreverpin");

  assert.equal(result.ok, true);
  assert.equal(result.value.name, "Forever Pin");
  assert.equal(captured.url, "/api/products/foreverpin/identity/refresh");
  assert.equal(captured.options.method, "POST");
  assert.equal(captured.options.credentials, "same-origin");
  assert.equal(captured.options.headers.get("X-Wheelhouse-Action"), "refresh");
  assert.equal(captured.options.body ?? null, null, "a refresh carries no body");
});

test("a refresh GitHub cannot serve fails with the host's reason and no product", async () => {
  globalThis.fetch = async () =>
    json(
      { title: "Service Unavailable", status: 503, detail: "The main branch of owner/forever-pin could not be read from GitHub." },
      503,
      { "Content-Type": "application/problem+json" },
    );

  const result = await identity.productsApi.refreshIdentity("foreverpin");

  assert.equal(result.ok, false);
  assert.equal(
    identity.failureReason(result.failure),
    "The main branch of owner/forever-pin could not be read from GitHub.",
  );
});

test("the descriptor route decodes deploy.yml's services, needs and sites, and a reason when it cannot be read", async () => {
  const urls = [];
  const answers = [
    { commit, problem: null, needsPostgres: true, services: [{ service: "desk", needs: ["postgres"] }, { service: "jobs" }],
      sites: [{ name: "app", exposure: "public", primary: true }, { name: "admin", exposure: "private" }] },
    { commit: null, problem: "owner/forever-pin@main has no engineering/deployment/deploy.yml.", services: [], sites: [] },
  ];
  globalThis.fetch = async (url) => {
    urls.push(url);
    return json({ data: answers[urls.length - 1] });
  };

  const read = await identity.productsApi.getDescriptor("ocharo-platform");
  const missing = await identity.productsApi.getDescriptor("foreverpin");

  assert.deepEqual(urls, ["/api/products/ocharo-platform/descriptor", "/api/products/foreverpin/descriptor"]);
  assert.deepEqual(read.value.services, [{ service: "desk", needs: ["postgres"] }, { service: "jobs", needs: [] }]);
  assert.deepEqual(read.value.sites.map((site) => [site.name, site.exposure, site.primary]),
    [["app", "public", true], ["admin", "private", false]]);
  assert.deepEqual(identity.ProductDescriptorRules.postgresServices(read.value), ["desk"]);
  assert.deepEqual(identity.ProductDescriptorRules.privateSites(read.value), ["admin"]);
  assert.deepEqual(identity.ProductDescriptorRules.serviceNames(read.value), ["desk", "jobs"]);
  assert.equal(missing.value.problem, "owner/forever-pin@main has no engineering/deployment/deploy.yml.");
  assert.equal((await (globalThis.fetch = async () => json({ data: { ...answers[0], sites: [{ name: "app", exposure: "open" }] } }),
    identity.productsApi.getDescriptor("x"))).ok, false, "an unknown exposure is a protocol error");
});

test("a slug is escaped in the refresh route", async () => {
  let url;
  globalThis.fetch = async (requested) => {
    url = requested;
    return json({ data: record });
  };
  await identity.productsApi.refreshIdentity("a/b c");
  assert.equal(url, "/api/products/a%2Fb%20c/identity/refresh");
});

test("a refresh updates the product list: the console shows the repository's identity afterwards", async () => {
  const renderer = identity.createRenderer({
    createElement: () => ({}),
    createText: () => ({}),
    createComment: () => ({}),
    insert() {},
    remove() {},
    patchProp() {},
    setText() {},
    setElementText() {},
    parentNode: () => null,
    nextSibling: () => null,
  });
  const requests = [];
  let refreshed = false;
  globalThis.fetch = async (url, options) => {
    requests.push(`${options.method ?? "GET"} ${url}`);
    if (options.method === "POST") {
      refreshed = true;
      return json({ data: fromManifest });
    }
    return json({ data: [refreshed ? fromManifest : record] });
  };

  let products;
  const app = renderer.createApp({
    setup() {
      products = identity.useProducts();
      return () => null;
    },
  });
  app.use(identity.queryPlugin(identity.queryClient));
  app.mount({});
  try {
    await products.reload();
    assert.equal(products.products.value[0].name, "ForeverPin");
    assert.equal(identity.isIdentityReadOnly(products.products.value[0]), false);

    const result = await products.refreshIdentity("foreverpin");
    assert.equal(result.ok, true);
    // The list is read again after the write, so every screen showing a product follows.
    for (let attempt = 0; attempt < 20 && products.products.value[0].name !== "Forever Pin"; attempt++)
      await new Promise((settle) => setImmediate(settle));

    assert.equal(products.products.value[0].name, "Forever Pin");
    assert.equal(identity.isIdentityReadOnly(products.products.value[0]), true);
    assert.deepEqual(requests, ["GET /api/products", "POST /api/products/foreverpin/identity/refresh", "GET /api/products"]);
  } finally {
    app.unmount();
  }
});
