import assert from "node:assert/strict";
import { after, afterEach, test } from "node:test";
import { resolve } from "node:path";
import { build, stop } from "esbuild";

// Bundle the real audit hook and the app's query client.
const bundle = await build({
  stdin: {
    contents: `
      export { useAuditEntries } from './src/application/audit';
      export { queryClient, queryPlugin } from './src/bootstrap/query';
      export { createRenderer, nextTick, ref } from 'vue';
    `,
    resolveDir: process.cwd(),
  },
  alias: { "@": resolve("src") },
  bundle: true,
  platform: "node",
  format: "esm",
  write: false,
});
const { useAuditEntries, queryClient, queryPlugin, createRenderer, nextTick, ref } =
  await import(`data:text/javascript;base64,${Buffer.from(bundle.outputFiles[0].text).toString("base64")}`);

const originalFetch = globalThis.fetch;
afterEach(() => {
  globalThis.fetch = originalFetch;
  queryClient.clear();
});
after(() => stop());

const renderer = createRenderer({
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

function json(value) {
  return new Response(JSON.stringify(value), { headers: { "Content-Type": "application/json" } });
}

function entry(sequence) {
  return { sequence, occurredAt: "2026-10-30T09:00:00Z", actor: "max", action: "deployment.start",
    subject: "foreverpin-dev", outcome: "succeeded", detail: null, reason: null };
}

/** Answers the trail two entries a page: 5 and 4, then 3 and 2, then 1 alone; each page names the token of the next. */
function serve() {
  const urls = [];
  globalThis.fetch = async (url) => {
    urls.push(url);
    const token = new URL(url, "http://localhost").searchParams.get("pageToken");
    return json({ data:
      token === null ? { items: [entry(5), entry(4)], pageSize: 2, nextPageToken: "t4" }
        : token === "t4" ? { items: [entry(3), entry(2)], pageSize: 2, nextPageToken: "t2" }
          : { items: [entry(1)], pageSize: 2 } });
  };
  return urls;
}

const settle = async () => {
  for (let round = 0; round < 5; round++) {
    await new Promise((done) => setImmediate(done));
    await nextTick();
  }
};

/** Mounts a panel that reads the trail as the Activity page does. */
function mount(product = ref(null)) {
  let entries;
  const app = renderer.createApp({
    setup() {
      entries = useAuditEntries(2, product);
      return () => null;
    },
  });
  app.use(queryPlugin(queryClient));
  app.mount({});
  return { app, entries: () => entries, product };
}

test("older entries load after the last page's token, newest first, until a page names none", async () => {
  const urls = serve();
  const panel = mount();
  try {
    await settle();
    assert.deepEqual(panel.entries().items.value.map((item) => item.sequence), [5, 4]);
    assert.equal(panel.entries().hasNextPage.value, true);

    await panel.entries().fetchNextPage();
    await settle();
    assert.deepEqual(panel.entries().items.value.map((item) => item.sequence), [5, 4, 3, 2]);
    assert.equal(panel.entries().hasNextPage.value, true);

    await panel.entries().fetchNextPage();
    await settle();
    assert.deepEqual(panel.entries().items.value.map((item) => item.sequence), [5, 4, 3, 2, 1]);
    assert.equal(panel.entries().hasNextPage.value, false);
    assert.deepEqual(urls,
      ["/api/audit?pageSize=2", "/api/audit?pageSize=2&pageToken=t4", "/api/audit?pageSize=2&pageToken=t2"]);
    assert.ok(urls.every((url) => !url.includes("before")), "nothing sends the old before cursor");
  } finally {
    panel.app.unmount();
  }
});

test("another product reads from its newest entries, not from the last token", async () => {
  const urls = serve();
  const panel = mount();
  try {
    await settle();
    await panel.entries().fetchNextPage();
    await settle();
    panel.product.value = "foreverpin";
    await settle();
    assert.equal(urls.at(-1), "/api/audit?pageSize=2&product=foreverpin");
    assert.deepEqual(panel.entries().items.value.map((item) => item.sequence), [5, 4]);
  } finally {
    panel.app.unmount();
  }
});
