import assert from "node:assert/strict";
import { after, afterEach, test } from "node:test";
import { stop } from "esbuild";
import { bundleModule } from "./BundleModule.mjs";

// The real session wiring: the console's bridge, HTTP client, query client and useAuth, over a stub renderer.
const {
  useAuth,
  productsApi,
  sessionBridge,
  queryClient,
  queryPlugin,
  useAppQuery,
  feedbackBus,
  timeoutManager,
  createRenderer,
  h,
  nextTick,
} = await bundleModule(`
  export { useAuth } from './src/application/auth';
  export { productsApi } from './src/integration/products';
  export { sessionBridge } from './src/integration/common';
  export { queryClient, queryPlugin, useAppQuery } from './src/bootstrap/query';
  export { feedbackBus } from '@wow-two-beta/fe/feedback';
  export { timeoutManager } from '@tanstack/vue-query';
  export { createRenderer, h, nextTick } from 'vue';
`);

// An expiry clears the cache while its panels are still mounted; their unmount then starts a garbage-collection
// timer on each removed query. It is harmless in the browser, but would hold this process open for five minutes.
timeoutManager.setTimeoutProvider({
  setTimeout: (callback, delay) => setTimeout(callback, delay).unref(),
  clearTimeout: (timer) => clearTimeout(timer),
  setInterval: (callback, delay) => setInterval(callback, delay).unref(),
  clearInterval: (timer) => clearInterval(timer),
});

const originalFetch = globalThis.fetch;
const owner = { login: "sulton-max", name: "Sulton", avatar: null };
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
let app;

afterEach(() => {
  app?.unmount();
  app = undefined;
  globalThis.fetch = originalFetch;
  delete globalThis.window;
  queryClient.clear();
  feedbackBus.reset();
});
after(() => stop());

function json(data, status = 200) {
  return new Response(JSON.stringify(data), { status, headers: { "Content-Type": "application/json" } });
}

function unauthorized() {
  return new Response(null, { status: 401 });
}

/** The browser the console runs in: its address, and the navigations sign-in asks for. */
function browserAt(pathname, search = "") {
  const assigned = [];
  globalThis.window = {
    location: { pathname, search, hash: "", assign: (url) => assigned.push(url) },
    addEventListener() {},
    removeEventListener() {},
  };
  return assigned;
}

async function settle() {
  for (let i = 0; i < 6; i++) {
    await new Promise((resolve) => setImmediate(resolve));
    await nextTick();
  }
}

/** Mounts the console's root as `App.vue` gates it: its panels render only while the owner is signed in. */
function mountConsole(panels) {
  let auth;
  const Panel = {
    props: { index: Number },
    setup(props) {
      useAppQuery({
        key: ["session-expiry", "panel", props.index],
        queryFn: ({ signal }) => productsApi.listProducts(signal),
      });
      return () => null;
    },
  };
  app = renderer.createApp({
    setup() {
      auth = useAuth();
      // An element root: the stub renderer has no siblings, so it cannot walk a fragment root to unmount it.
      return () =>
        h("div", auth.user.value ? Array.from({ length: panels }, (_, index) => h(Panel, { index })) : []);
    },
  });
  app.use(queryPlugin(queryClient));
  app.mount({});
  return () => auth;
}

test("parallel 401s of a signed-in session sign the owner out once, with one neutral notice and no error toast", async () => {
  const panels = 5;
  browserAt("/p/foreverpin", "?env=dev");
  const held = [];
  globalThis.fetch = (url) => {
    if (String(url).endsWith("/api/identity/me")) return Promise.resolve(json(owner));
    // The server session ended: every private read is refused, and all of them are in flight together.
    return new Promise((resolve) => {
      held.push(resolve);
      if (held.length === panels) for (const release of held) release(unauthorized());
    });
  };
  const notices = [];
  const stopNotices = feedbackBus.subscribe((notice) => notices.push(notice));
  const expiries = [];
  const stopExpiries = sessionBridge.subscribeExpired((expiry) => expiries.push(expiry));
  try {
    const auth = mountConsole(panels);
    await settle();
    assert.equal(held.length, panels, "every panel reads while the owner is signed in");
    await settle();

    assert.equal(expiries.length, 1, "the session expires once");
    assert.equal(expiries[0].returnUrl, "/p/foreverpin?env=dev");
    assert.equal(auth().user.value, null, "App.vue shows the sign-in screen in place");
    assert.equal(auth().error.value, null, "an expiry is not a session failure");
    assert.equal(sessionBridge.getSession().status, "anonymous");
    assert.deepEqual(
      notices.map((notice) => [notice.id, notice.tone]),
      [["session-expired", "info"]],
      "one neutral notice, no error toast per 401",
    );
    assert.equal(queryClient.getQueryCache().getAll().length, 0, "the expired session's records are cleared");

    // A 401 while signed out is swallowed: no second expiry, no notice.
    globalThis.fetch = async () => unauthorized();
    const late = await productsApi.listProducts();
    assert.equal(late.ok, false);
    assert.equal(expiries.length, 1);
    assert.equal(notices.length, 1);
  } finally {
    stopNotices();
    stopExpiries();
  }
});

test("signing in after an expiry returns to the page and its query, which reads again", async () => {
  const assigned = browserAt("/p/foreverpin/secrets", "?env=prod&item=DATABASE_URL");
  let session = "signed-in";
  const reads = [];
  globalThis.fetch = async (url) => {
    const path = String(url);
    if (path.endsWith("/api/identity/me")) return session === "signed-in" ? json(owner) : unauthorized();
    reads.push(path);
    return session === "signed-in" ? json({ data: [] }) : unauthorized();
  };
  let auth = mountConsole(1);
  await settle();
  assert.equal(auth().user.value?.login, owner.login);

  session = "expired";
  await queryClient.refetchQueries();
  await settle();
  assert.equal(auth().user.value, null);

  auth().signIn();
  assert.deepEqual(assigned, [
    `/api/identity/sign-in?returnUrl=${encodeURIComponent("/p/foreverpin/secrets?env=prod&item=DATABASE_URL")}`,
  ]);

  // GitHub sends the browser back to that address: the console loads again and asks who is signed in afresh.
  app.unmount();
  session = "signed-in";
  const before = reads.length;
  auth = mountConsole(1);
  await settle();
  assert.equal(auth().user.value?.login, owner.login, "the next session read is not the expired one");
  assert.equal(sessionBridge.getSession().status, "authenticated");
  assert.ok(reads.length > before, "the page reads its data again");
});

test("signing out of an already-expired session reports no failure", async () => {
  browserAt("/servers");
  globalThis.fetch = async (url, init) => {
    if (String(url).endsWith("/api/identity/me")) return json(owner);
    if (init?.method === "POST") return unauthorized();
    return json({ data: [] });
  };
  const auth = mountConsole(0);
  await settle();
  assert.equal(auth().user.value?.login, owner.login);

  await auth().signOut();
  assert.equal(auth().user.value, null);
  assert.equal(auth().error.value, null, "the server session was already gone");
  assert.equal(sessionBridge.getSession().status, "anonymous");
});
