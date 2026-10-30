import assert from "node:assert/strict";
import { after, afterEach, test } from "node:test";
import { stop } from "esbuild";
import { bundleModule } from "./BundleModule.mjs";

// The console's view choices on the SDK storage broker: what the console stored before v0.10 — raw strings beside
// JSON — still reads back, and every choice is written as JSON from now on.

/** A `localStorage` stand-in; `blocked` makes every access throw, like a browser that refuses storage. */
function storage(entries = {}, { blocked = false } = {}) {
  const values = new Map(Object.entries(entries));
  const guard = () => {
    if (blocked) throw new Error("SecurityError");
  };
  return {
    values,
    getItem: (key) => (guard(), values.has(key) ? values.get(key) : null),
    setItem: (key, value) => (guard(), values.set(key, String(value))),
    removeItem: (key) => (guard(), values.delete(key)),
    clear: () => (guard(), values.clear()),
  };
}

let instance = 0;

/** Loads a fresh copy of the preferences module over `store`, as a page load would. */
async function loadPreferences(store) {
  const window = new EventTarget();
  Object.defineProperty(window, "localStorage", {
    get: () => {
      if (store.blocked) throw new Error("SecurityError");
      return store;
    },
  });
  globalThis.window = window;
  instance += 1;
  return bundleModule(`
    export * from './src/application/preferences';
    export const instance = ${instance};
  `);
}

afterEach(() => {
  delete globalThis.window;
});
after(() => stop());

const notices = Array.from({ length: 25 }, (_, index) => `d-${index}`);

test("every choice the console stored before the move reads back, raw strings included", async () => {
  const store = storage({
    "wheelhouse.show-inactive": "true",
    "wheelhouse.sidebar-collapsed": "true",
    "wheelhouse.show-all-attempts": "true",
    "wheelhouse.environment": "prod",
    "wheelhouse.products-view": "rows",
    "wheelhouse.products-collapsed-workspaces": JSON.stringify(["w-ventures", 7]),
    "wheelhouse.last-product": JSON.stringify({ slug: "wheelhouse", tab: "settings" }),
    "wheelhouse.self-deploy-dismissed": JSON.stringify(notices),
  });
  const preferences = await loadPreferences(store);

  assert.equal(preferences.useShowInactive().value, true);
  assert.equal(preferences.useSidebarCollapsed().value, true);
  assert.equal(preferences.useShowAllAttempts().value, true);
  assert.equal(preferences.useLastEnvironment().value, "prod");
  assert.equal(preferences.useProductsView().value, "rows");
  assert.equal(preferences.useGroupExpansion().isExpanded("w-ventures"), false);
  assert.equal(preferences.useGroupExpansion().isExpanded("w-platform"), true);
  assert.deepEqual(preferences.useLastProduct().value, { slug: "wheelhouse", tab: "settings" });
  assert.deepEqual(preferences.useDismissedSelfNotices().value, notices.slice(-20));

  // The raw strings are re-encoded once; the JSON values stay as they were.
  assert.equal(store.values.get("wheelhouse.environment"), '"prod"');
  assert.equal(store.values.get("wheelhouse.products-view"), '"rows"');
  assert.equal(store.values.get("wheelhouse.show-inactive"), "true");

  // The next load finds JSON and reads the same choices.
  const reloaded = await loadPreferences(store);
  assert.equal(reloaded.useLastEnvironment().value, "prod");
  assert.equal(reloaded.useProductsView().value, "rows");
  assert.equal(store.values.get("wheelhouse.environment"), '"prod"');
});

test("an empty, retired or unknown stored choice reads as the default", async () => {
  const store = storage({
    "wheelhouse.environment": "",
    "wheelhouse.products-view": "tiles",
    "wheelhouse.show-inactive": "yes",
    "wheelhouse.last-product": JSON.stringify({ slug: "" }),
  });
  const preferences = await loadPreferences(store);

  assert.equal(preferences.useLastEnvironment().value, null);
  assert.equal(store.values.has("wheelhouse.environment"), false);
  assert.equal(preferences.useProductsView().value, "cards");
  assert.equal(preferences.useShowInactive().value, false);
  assert.equal(preferences.useLastProduct().value, null);

  const retired = await loadPreferences(storage({ "wheelhouse.environment": "test" }));
  assert.equal(retired.useLastEnvironment().value, null);
});

test("a changed choice is written as JSON, and the dismissed notices keep the twenty newest", async () => {
  const store = storage();
  const preferences = await loadPreferences(store);

  preferences.useProductsView().value = "rows";
  preferences.useLastEnvironment().value = "dev";
  preferences.useSidebarCollapsed().value = true;
  preferences.useGroupExpansion().toggle("w-ventures");
  preferences.useLastProduct().value = { slug: "foreverpin", tab: null };
  preferences.useDismissedSelfNotices().value = notices;

  assert.equal(store.values.get("wheelhouse.products-view"), '"rows"');
  assert.equal(store.values.get("wheelhouse.environment"), '"dev"');
  assert.equal(store.values.get("wheelhouse.sidebar-collapsed"), "true");
  assert.equal(store.values.get("wheelhouse.products-collapsed-workspaces"), '["w-ventures"]');
  assert.equal(store.values.get("wheelhouse.last-product"), '{"slug":"foreverpin","tab":null}');
  assert.deepEqual(JSON.parse(store.values.get("wheelhouse.self-deploy-dismissed")), notices.slice(-20));
  assert.deepEqual(preferences.useDismissedSelfNotices().value, notices.slice(-20));

  preferences.useGroupExpansion().expand("w-ventures");
  assert.equal(store.values.get("wheelhouse.products-collapsed-workspaces"), "[]");
});

test("blocked storage keeps the defaults and holds a choice for the visit", async () => {
  const store = storage({ "wheelhouse.products-view": "rows" }, { blocked: true });
  store.blocked = true;
  const preferences = await loadPreferences(store);

  assert.equal(preferences.useProductsView().value, "cards");
  preferences.useProductsView().value = "rows";
  assert.equal(preferences.useProductsView().value, "rows");
});
