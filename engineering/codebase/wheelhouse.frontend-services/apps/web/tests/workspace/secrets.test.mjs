import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import ts from "typescript";
import { parse, compileScript } from "@vue/compiler-sfc";
import { createRenderer, effectScope, nextTick, ref } from "vue";

const asModule = (source) =>
  `data:text/javascript;base64,${Buffer.from(source).toString("base64")}`;
const apiUrl = asModule(`
export const state = { calls: [], operation: async () => ({ ok: true, value: {} }) };
export const secretsApi = new Proxy({}, { get: (_target, name) => (...args) => {
  state.calls.push({ name, args }); return state.operation(name, args);
} });
`);
const cacheUrl = asModule(`
import { ref, computed } from '${import.meta.resolve("vue")}';
export const state = { invalidated: [], fail: false };
export function useQueryCache() { return { invalidate: async (key) => {
  state.invalidated.push(key); if(state.fail) throw new Error('metadata refresh failed');
} }; }
export function useAppQuery() {
  return { data: ref([]), loading: computed(() => false), error: computed(() => null), refetch() {} };
}
`);
const aliases = new Map([
  ["@/integration/secrets", apiUrl],
  ["@/bootstrap/query", cacheUrl],
  [
    "@/bootstrap/form",
    import.meta.resolve("@wow-two-beta/ui-vue/forms-engine/house"),
  ],
]);

/** Loads actual application modules against isolated HTTP/cache ports and the published Vue SDK. */
async function loadSource(relative, isSfc = false) {
  const source = await readFile(
    new URL(`../../${relative}`, import.meta.url),
    "utf8",
  );
  const code = isSfc
    ? compileScript(parse(source, { filename: relative }).descriptor, {
        id: relative,
      }).content
    : source;
  const { outputText } = ts.transpileModule(code, {
    compilerOptions: {
      module: ts.ModuleKind.ESNext,
      target: ts.ScriptTarget.ES2022,
    },
  });
  return asModule(
    outputText.replace(
      /(from\s+["'])([^"']+)(["'])/g,
      (_match, before, name, after) => {
        const resolved = aliases.get(name) ?? import.meta.resolve(name);
        return `${before}${resolved}${after}`;
      },
    ),
  );
}

const keyUrl = await loadSource("src/application/secrets/SecretKeys.ts");
aliases.set("./SecretKeys", keyUrl);
const changeUrl = await loadSource(
  "src/application/secrets/useVaultChanges.ts",
);
const metadataUrl = await loadSource(
  "src/application/secrets/useVaultMetadata.ts",
);
aliases.set(
  "@/application/secrets",
  asModule(`export * from '${changeUrl}'; export * from '${metadataUrl}';`),
);
const formsUrl = await loadSource("src/application/secrets/VaultForms.ts");
aliases.set("@/application/secrets/VaultForms", formsUrl);
const { useSecretChanges, useTokenChanges } = await import(changeUrl);
const { state: api } = await import(apiUrl);
const { state: cache } = await import(cacheUrl);
const modals = Object.fromEntries(
  await Promise.all(
    ["NamespaceModal", "SetSecretModal", "MintTokenModal"].map(async (name) => {
      const url = await loadSource(
        `src/presentation/secrets/components/${name}.vue`,
        true,
      );
      aliases.set(`./${name}.vue`, url);
      return [name, (await import(url)).default];
    }),
  ),
);
aliases.set(
  "@/presentation/common/components",
  asModule(
    "const Stub = { render: () => null }; export { Stub as LoadState, Stub as SkeletonStateSlot };",
  ),
);
modals.TokensTable = (
  await import(
    await loadSource(
      "src/presentation/secrets/components/TokensTable.vue",
      true,
    )
  )
).default;

const renderer = createRenderer({
  patchProp() {},
  insert() {},
  remove() {},
  createElement: () => ({}),
  createText: () => ({}),
  createComment: () => ({}),
  setText() {},
  setElementText() {},
  parentNode: () => null,
  nextSibling: () => null,
  querySelector: () => null,
  setScopeId() {},
  insertStaticContent: () => [{}, {}],
});

/** Mounts real SFC setup/lifecycle code without substituting the form engine or requiring a browser DOM. */
function mountModal(name, extra = {}) {
  let app;
  const events = [];
  const component = { ...modals[name], render: () => null };
  app = renderer.createApp(component, {
    vault: "vault-a",
    ns: "namespace-a",
    open: true,
    ...extra,
    "onUpdate:open": (open) => {
      events.push(open);
      app._instance.props.open = open;
    },
    onCreated: (slug) => events.push(slug),
  });
  app.mount({});
  return {
    app,
    state: app._instance.setupState,
    props: app._instance.props,
    events,
    unmount: () => app.unmount(),
  };
}

function reset() {
  api.calls.length = 0;
  api.operation = async () => ({ ok: true, value: {} });
  cache.invalidated.length = 0;
  cache.fail = false;
}

function deferred() {
  let resolve;
  const promise = new Promise((done) => {
    resolve = done;
  });
  return { promise, resolve };
}

const failure = {
  ok: false,
  failure: {
    type: "unexpected",
    code: "http",
    status: 503,
    headers: {},
    problem: null,
    message: "Vault is temporarily unavailable.",
  },
};
const token = {
  id: "token-1",
  name: "management",
  namespace: "namespace-a",
  token: "test-one-time-plaintext",
};
const tick = () => new Promise((resolve) => setImmediate(resolve));

// node:test runs these top-level cases sequentially; every fixture owns a fresh scope and isolated ports.
test("successful secret writes invalidate metadata only and never retain values in the change object", async () => {
  reset();
  const scope = effectScope();
  const changes = scope.run(() => useSecretChanges("vault-a", "namespace-a"));
  api.operation = async () => ({
    ok: true,
    value: { key: "DATABASE_URL", version: 2 },
  });
  const result = await changes.set(
    "DATABASE_URL",
    "test-write-only-value",
    "Connection",
  );
  assert.equal(result.ok, true);
  assert.deepEqual(cache.invalidated, [
    ["vaults", "vault-a", "secrets", "namespace-a"],
    ["vaults", "vault-a", "hygiene"],
  ]);
  assert.equal("data" in changes, false);
  assert.equal("variables" in changes, false);
  assert.equal(
    JSON.stringify(cache.invalidated).includes("test-write-only-value"),
    false,
  );
  scope.stop();
});

test("namespace switching aborts pending token minting and discards a late plaintext response", async () => {
  reset();
  const request = deferred();
  api.operation = () => request.promise;
  const scope = effectScope();
  const ns = ref("namespace-a");
  const changes = scope.run(() => useTokenChanges("vault-a", ns));
  const operation = changes.mint("management");
  const signal = api.calls[0].args.at(-1);
  ns.value = "namespace-b";
  await nextTick();
  assert.equal(signal.aborted, true);
  request.resolve({ ok: true, value: token });
  const result = await operation;
  assert.equal(result.ok, false);
  assert.equal(result.failure.code, "cancelled");
  assert.deepEqual(cache.invalidated, []);
  assert.equal(changes.loading.value, false);
  scope.stop();
});

test("scope disposal aborts writes and suppresses stale error state", async () => {
  reset();
  const request = deferred();
  api.operation = () => request.promise;
  const scope = effectScope();
  const changes = scope.run(() => useSecretChanges("vault-a", "namespace-a"));
  const operation = changes.setDisabled("DATABASE_URL", true);
  const signal = api.calls[0].args.at(-1);
  scope.stop();
  assert.equal(signal.aborted, true);
  request.resolve(failure);
  assert.equal((await operation).failure.code, "cancelled");
  assert.equal(changes.error.value, null);
});

test("failed metadata refresh cannot turn a committed mint into a failed mint", async () => {
  reset();
  cache.fail = true;
  api.operation = async () => ({ ok: true, value: token });
  const scope = effectScope();
  const changes = scope.run(() => useTokenChanges("vault-a", "namespace-a"));
  assert.equal((await changes.mint("management")).ok, true);
  await tick();
  assert.equal(changes.error.value, null);
  scope.stop();
});

test("failed secret submission retains editable values and keeps the modal open", async () => {
  reset();
  api.operation = async () => failure;
  const modal = mountModal("SetSecretModal");
  modal.state.form.setValue("key", "DATABASE_URL");
  modal.state.form.setValue("value", "test-retry-value");
  modal.state.form.setValue("description", "Connection");
  await modal.state.submit(new Event("submit", { cancelable: true }));
  assert.equal(modal.props.open, true);
  assert.equal(modal.state.form.values.value, "test-retry-value");
  assert.equal(
    modal.state.form.state.submitError.message,
    failure.failure.message,
  );
  assert.deepEqual(modal.events, []);
  modal.state.close(false);
  assert.equal(modal.state.form.values.value, "");
  assert.equal(modal.state.form.values.description, "");
  modal.unmount();
});

test("successful secret submission closes and erases plaintext from current and reset baseline", async () => {
  reset();
  const modal = mountModal("SetSecretModal", { secretKey: "DATABASE_URL" });
  modal.state.form.setValue("value", "test-submitted-value");
  await modal.state.submit(new Event("submit", { cancelable: true }));
  assert.equal(modal.props.open, false);
  assert.equal(modal.state.form.values.value, "");
  modal.state.form.reset();
  assert.equal(modal.state.form.values.value, "");
  assert.equal(api.calls[0].args[2], "DATABASE_URL");
  modal.unmount();
});

test("secret editor changes clear plaintext and do not accept a late successful submit", async () => {
  reset();
  const request = deferred();
  api.operation = () => request.promise;
  const modal = mountModal("SetSecretModal");
  modal.state.form.setValue("key", "DATABASE_URL");
  modal.state.form.setValue("value", "test-pending-value");
  const submitted = modal.state.submit(
    new Event("submit", { cancelable: true }),
  );
  await tick();
  modal.props.ns = "namespace-b";
  assert.equal(modal.state.form.values.value, "");
  request.resolve({ ok: true, value: {} });
  await submitted;
  assert.deepEqual(modal.events, []);
  assert.equal(modal.props.open, true);
  modal.unmount();
});

test("minted token is revealed locally once and forgotten on close and namespace changes", async () => {
  reset();
  api.operation = async () => ({ ok: true, value: token });
  const modal = mountModal("MintTokenModal");
  modal.state.form.setValue("name", "management");
  assert.equal(await modal.state.form.handleSubmit(), true);
  assert.equal(modal.state.minted.token, token.token);
  assert.equal(
    JSON.stringify(modal.state.form.values).includes(token.token),
    false,
  );
  modal.props.ns = "namespace-b";
  assert.equal(modal.state.minted, null);
  assert.equal(modal.state.form.values.name, "");
  modal.state.form.setValue("name", "management");
  await modal.state.form.handleSubmit();
  modal.state.close(false);
  assert.equal(modal.state.minted, null);
  modal.props.open = true;
  assert.equal(modal.state.minted, null);
  modal.unmount();
});

test("closing during mint discards a successful late token instead of reopening the reveal", async () => {
  reset();
  const request = deferred();
  api.operation = () => request.promise;
  const modal = mountModal("MintTokenModal");
  modal.state.form.setValue("name", "management");
  const submitted = modal.state.form.handleSubmit();
  await tick();
  modal.state.close(false);
  request.resolve({ ok: true, value: token });
  assert.equal(await submitted, false);
  assert.equal(modal.state.minted, null);
  assert.equal(modal.props.open, false);
  modal.unmount();
});

test("namespace validation prevents empty submissions and preserves failed input", async () => {
  reset();
  const modal = mountModal("NamespaceModal");
  assert.equal(await modal.state.form.handleSubmit(), false);
  assert.equal(api.calls.length, 0);
  api.operation = async () => failure;
  modal.state.form.setValue("slug", "staging");
  modal.state.form.setValue("name", "Staging");
  await modal.state.submit(new Event("submit", { cancelable: true }));
  assert.equal(modal.props.open, true);
  assert.deepEqual(modal.events, []);
  assert.equal(modal.state.form.values.slug, "staging");
  api.operation = async () => ({ ok: true, value: {} });
  await modal.state.submit(new Event("submit", { cancelable: true }));
  assert.deepEqual(modal.events, ["staging", false]);
  modal.unmount();
});

test("failed revocation keeps its confirmation open until a successful retry", async () => {
  reset();
  api.operation = async () => failure;
  const table = mountModal("TokensTable", { flags: new Map() });
  table.state.revoking = {
    id: "token-1",
    name: "management",
    isRevoked: false,
  };
  await table.state.revoke();
  assert.equal(table.state.revoking.id, "token-1");
  assert.equal(table.state.changeError.message, failure.failure.message);
  api.operation = async () => ({ ok: true, value: {} });
  await table.state.revoke();
  assert.equal(table.state.revoking, null);
  assert.equal(table.state.changeError, null);
  table.unmount();
});

test("closing pending revoke aborts transport and prevents errors leaking into another confirmation", async () => {
  reset();
  const request = deferred();
  api.operation = () => request.promise;
  const table = mountModal("TokensTable", { flags: new Map() });
  table.state.revoking = {
    id: "token-1",
    name: "management",
    isRevoked: false,
  };
  const revoked = table.state.revoke();
  const signal = api.calls[0].args.at(-1);
  table.state.closeRevoke(false);
  table.state.revoking = {
    id: "token-2",
    name: "replacement",
    isRevoked: false,
  };
  request.resolve(failure);
  await revoked;
  assert.equal(signal.aborted, true);
  assert.equal(table.state.revoking.id, "token-2");
  assert.equal(table.state.changeError, null);
  table.unmount();
});

test("repeated manual mint submission sends only one request while its snapshot is pending", async () => {
  reset();
  const request = deferred();
  api.operation = () => request.promise;
  const modal = mountModal("MintTokenModal");
  modal.state.form.setValue("name", "management");
  const first = modal.state.form.handleSubmit();
  const repeated = modal.state.form.handleSubmit();
  await tick();
  assert.equal(api.calls.length, 1);
  request.resolve({ ok: true, value: token });
  await Promise.all([first, repeated]);
  assert.equal(modal.state.minted.token, token.token);
  modal.unmount();
});
