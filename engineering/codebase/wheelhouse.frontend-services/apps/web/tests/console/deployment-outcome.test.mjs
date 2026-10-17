import assert from "node:assert/strict";
import { after, afterEach, test } from "node:test";
import { resolve } from "node:path";
import { build, stop } from "esbuild";

// Bundle the real outcome hook, the history read beside it and the app's query client.
const bundle = await build({
  stdin: {
    contents: `
      export { useDeploymentHistory, useDeploymentOutcome } from './src/application/deployments';
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
const { useDeploymentHistory, useDeploymentOutcome, queryClient, queryPlugin, createRenderer, nextTick, ref } =
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

const JobId = "11111111-1111-4111-8111-111111111111";

function json(value) {
  return new Response(JSON.stringify(value), { headers: { "Content-Type": "application/json" } });
}

function job(status) {
  return { id: JobId, status, targetId: "foreverpin-dev", release: "v1.0.0", submittedAt: "2026-10-17T09:00:00Z" };
}

/** Answers the history and the job's outcome, counting each read; `statuses` are the outcome's answers in turn. */
function serve(statuses) {
  const reads = { history: 0, outcome: 0 };
  globalThis.fetch = async (url) => {
    if (url === "/api/deployments") {
      reads.history++;
      return json({ data: [job(statuses[Math.min(reads.outcome, statuses.length - 1)])] });
    }
    assert.equal(url, `/api/deployments/${JobId}`);
    return json({ data: job(statuses[Math.min(reads.outcome++, statuses.length - 1)]) });
  };
  return reads;
}

/** Mounts an inspector that reads the history and follows the job, as the workspace does. */
function inspect(listed) {
  let outcome;
  const id = ref(null);
  const app = renderer.createApp({
    setup() {
      useDeploymentHistory();
      outcome = useDeploymentOutcome(id, listed);
      return () => null;
    },
  });
  app.use(queryPlugin(queryClient));
  app.mount({});
  return { app, id, outcome: () => outcome };
}

const settle = async () => {
  for (let round = 0; round < 5; round++) {
    await new Promise((done) => setImmediate(done));
    await nextTick();
  }
};

test("opening a job that already finished loads the history once and refetches nothing", async () => {
  const reads = serve(["succeeded"]);
  const inspector = inspect(() => "succeeded");
  try {
    await settle();
    inspector.id.value = JobId;
    await settle();
    assert.equal(inspector.outcome().data.value.status, "succeeded");
    assert.deepEqual(reads, { history: 1, outcome: 1 });
  } finally {
    inspector.app.unmount();
  }
});

test("a job listed as running that turns out finished refreshes the reads beside it once", async () => {
  const reads = serve(["succeeded"]);
  const inspector = inspect(() => "running");
  try {
    await settle();
    inspector.id.value = JobId;
    await settle();
    assert.deepEqual(reads, { history: 2, outcome: 1 });
  } finally {
    inspector.app.unmount();
  }
});

test("a followed job refreshes the reads beside it when it finishes, not while it runs", async () => {
  const reads = serve(["queued", "running", "succeeded"]);
  const inspector = inspect(() => "queued");
  try {
    await settle();
    inspector.id.value = JobId;
    await settle();
    assert.equal(reads.history, 1);
    await inspector.outcome().refetch();
    await settle();
    assert.equal(reads.history, 1);
    await inspector.outcome().refetch();
    await settle();
    assert.equal(inspector.outcome().data.value.status, "succeeded");
    assert.equal(reads.history, 2);
    // Finished, so a later read of the same job changes nothing.
    await inspector.outcome().refetch();
    await settle();
    assert.equal(reads.history, 2);
  } finally {
    inspector.app.unmount();
  }
});
