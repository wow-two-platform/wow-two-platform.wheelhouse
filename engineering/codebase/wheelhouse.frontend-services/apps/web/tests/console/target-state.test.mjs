import assert from "node:assert/strict";
import { after, afterEach, test } from "node:test";
import { resolve } from "node:path";
import { build, stop } from "esbuild";

// Bundle the real state read, the control that follows a reset, and the app's query client.
const bundle = await build({
  stdin: {
    contents: `
      export { DeploymentKeys, useTargetControl, useTargetState } from './src/application/deployments';
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
const { DeploymentKeys, useTargetControl, useTargetState, queryClient, queryPlugin, createRenderer, nextTick, ref } =
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

const Prod = "ocharo-prod";
const Other = "keysmith-prod";
const ResetJob = "22222222-2222-4222-8222-222222222222";

function json(value, status = 200) {
  return new Response(JSON.stringify(value), {
    status,
    headers: { "Content-Type": status === 200 ? "application/json" : "application/problem+json" },
  });
}

/** A target's state as the runner answers it: the identity set at `level`, read by `source`. */
function state(target, level, source) {
  return {
    targetId: target,
    project: target,
    condition: "ready",
    current: { id: "11111111-1111-4111-8111-111111111111", release: "platform v0.1.11", status: "succeeded" },
    active: null,
    migrations: {
      updatedAt: "2026-11-01T11:30:00Z",
      sets: [{ name: "identity", schema: "identity", service: "identity", level, newestLabel: null, newestKind: null,
        state: level ? "current" : "behind", rollbackFloor: null, release: "platform v0.1.11", job: null, source,
        at: "2026-11-01T11:30:00Z" }],
    },
  };
}

/** Answers each target's state and the reset's outcome as the console does; anything else is the API's own 404,
 * as an empty target segment (`/targets//state`) gets. Every requested path is kept. */
function serve() {
  const served = { requests: [], level: 3, source: "migrate" };
  globalThis.fetch = async (url) => {
    served.requests.push(url);
    const match = /^\/api\/deployments\/targets\/([a-z0-9-]+)\/state$/.exec(url);
    if (match) return json({ data: state(match[1], served.level, served.source) });
    if (url === `/api/deployments/${ResetJob}`)
      return json({ data: { id: ResetJob, status: "succeeded", targetId: Prod, release: "platform v0.1.11",
        submittedAt: "2026-11-01T11:31:00Z" } });
    return json({ title: "Not Found", status: 404 }, 404);
  };
  return served;
}

/** Mounts one surface of the console on the shared query client; `use` runs in its setup. */
function mount(use) {
  let value;
  const app = renderer.createApp({
    setup() {
      value = use();
      return () => null;
    },
  });
  app.use(queryPlugin(queryClient));
  app.mount({});
  return { app, value: () => value };
}

const settle = async () => {
  for (let round = 0; round < 5; round++) {
    await new Promise((done) => setImmediate(done));
    await nextTick();
  }
};

/** The overview reads its environment's state; the deploy dialog reads the same target's state only while its prod
 * confirm step is open, then moves on, as a refused prod deploy leaves it. */
async function overviewAfterADeployDialog(served) {
  const overview = mount(() => useTargetState(() => Prod));
  const step = ref("choose");
  const chosen = ref(Prod);
  const dialog = mount(() => useTargetState(() => (step.value === "confirm" ? chosen.value : null)));
  await settle();
  step.value = "confirm";
  await settle();
  step.value = "follow";
  await settle();
  assert.equal(overview.value().data.value.migrations.sets[0].level, served.level);
  return { overview, dialog, step, chosen };
}

test("a deploy dialog that read prod's state leaves the overview's later reads on that target", async () => {
  const served = serve();
  const { overview, dialog, step, chosen } = await overviewAfterADeployDialog(served);
  try {
    // The refused rollout ends: the outcome refreshes the target's state.
    await queryClient.invalidateQueries({ queryKey: DeploymentKeys.state(Prod) });
    await settle();
    assert.equal(served.requests.filter((url) => url.includes("/targets//")).length, 0, served.requests.join("\n"));
    assert.equal(overview.value().error.value, null);
    assert.equal(overview.value().data.value.targetId, Prod);

    // The dialog then confirms another prod target: prod's next refresh still reads prod, never that target.
    chosen.value = Other;
    step.value = "confirm";
    await settle();
    await queryClient.invalidateQueries({ queryKey: DeploymentKeys.state(Prod) });
    await settle();
    assert.equal(overview.value().data.value.targetId, Prod);
    assert.equal(overview.value().error.value, null);
  } finally {
    overview.app.unmount();
    dialog.app.unmount();
  }
});

test("the overview's migration sets show a set reset once its control ends", async () => {
  const served = serve();
  const { overview, dialog } = await overviewAfterADeployDialog(served);
  const control = mount(() => useTargetControl());
  try {
    // The set reset ran on the target: identity is at 0, read by the reset.
    served.level = 0;
    served.source = "reset";
    const ended = await control.value().follow(ResetJob, Prod);
    assert.equal(ended.status, "succeeded");
    await settle();
    assert.equal(overview.value().error.value, null);
    const [set] = overview.value().data.value.migrations.sets;
    assert.deepEqual([set.level, set.state, set.source], [0, "behind", "reset"]);
  } finally {
    overview.app.unmount();
    dialog.app.unmount();
    control.app.unmount();
  }
});
