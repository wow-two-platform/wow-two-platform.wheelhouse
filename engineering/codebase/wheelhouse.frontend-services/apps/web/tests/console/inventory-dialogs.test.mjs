import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { resolve } from "node:path";
import { after, test } from "node:test";
import { build, stop } from "esbuild";
import { compileScript, parse } from "@vue/compiler-sfc";
import { createRenderer, nextTick, reactive } from "vue";

// Bundle the real inventory dialogs' setup code; packages stay external and resolve to the installed copies, so the
// dialogs and this harness share one Vue. Templates are not compiled: the checks read form state, not markup.
const bundle = await build({
  stdin: {
    contents: `
      export { default as ProductFormModal } from './src/presentation/products/components/ProductFormModal.vue';
      export { default as TargetFormModal } from './src/presentation/products/components/TargetFormModal.vue';
      export { default as ServerFormModal } from './src/presentation/servers/components/ServerFormModal.vue';
      export { default as VaultFormModal } from './src/presentation/servers/components/VaultFormModal.vue';
    `,
    resolveDir: process.cwd(),
  },
  alias: { "@": resolve("src") },
  bundle: true,
  packages: "external",
  platform: "node",
  format: "esm",
  write: false,
  plugins: [
    {
      name: "sfc-setup",
      setup(build) {
        build.onLoad({ filter: /\.vue$/ }, async (args) => {
          const source = await readFile(args.path, "utf8");
          const { descriptor } = parse(source, { filename: args.path });
          return { contents: compileScript(descriptor, { id: args.path }).content, loader: "ts" };
        });
      },
    },
  ],
});
const code = bundle.outputFiles[0].text.replace(
  /(from\s+["'])([^"'./][^"']*)(["'])/g,
  (_match, before, name, end) => `${before}${import.meta.resolve(name)}${end}`,
);
const dialogs = await import(`data:text/javascript;base64,${Buffer.from(code).toString("base64")}`);
after(() => stop());

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

const unused = async () => ({ ok: true, value: {} });
const operations = { create: unused, update: unused, remove: unused };

/**
 * Renders a dialog under a compiled parent template that binds `open` first, as the console's pages do, so an update
 * writes the props in that order. "New" clears the record and opens the dialog in one update.
 */
function mountDialog(dialog, props) {
  const state = reactive({ open: false, ...props });
  const bindings = Object.keys(state)
    .map((name) => `:${name}="state.${name}"`)
    .join(" ");
  let instance;
  const app = renderer.createApp({
    components: { Dialog: { ...dialog, render: () => null } },
    template: `<Dialog ${bindings} :operations="operations" @update:open="close" @vue:mounted="capture" />`,
    setup: () => ({
      state,
      operations,
      close: (open) => (state.open = open),
      capture: (vnode) => (instance = vnode.component),
    }),
  });
  app.mount({});
  return { state, values: () => instance.setupState.form.state.values, unmount: () => app.unmount() };
}

/** Edits a record, closes, then opens "New" in one update, and returns the values each session started from. */
async function editThenNew(dialog, props, recordProp, record) {
  const mounted = mountDialog(dialog, { ...props, [recordProp]: null });
  Object.assign(mounted.state, { [recordProp]: record, open: true });
  await nextTick();
  const edited = { ...mounted.values() };
  mounted.state.open = false;
  await nextTick();
  Object.assign(mounted.state, { [recordProp]: null, open: true });
  await nextTick();
  const created = { ...mounted.values() };
  mounted.unmount();
  return { edited, created };
}

test("New product opens empty right after editing a product", async () => {
  const product = {
    slug: "foreverpin",
    name: "ForeverPin",
    description: "Styled QR codes.",
    id: "p-test",
    workspaceId: "w-ventures",
    status: "active",
    inactiveServices: [],
    repository: { name: "owner/forever-pin", url: "https://github.com/owner/forever-pin", defaultBranch: "main" },
    release: { asset: "foreverpin-release.tar.gz", workflow: null, images: [] },
    homepage: null,
    identity: { source: "record", commit: null, problem: null, services: [] },
    iconUrl: "/api/products/foreverpin/icon",
    environments: [],
  };
  const { edited, created } = await editThenNew(dialogs.ProductFormModal, {}, "product", product);
  assert.equal(edited.name, "ForeverPin");
  assert.equal(created.slug, "");
  assert.equal(created.name, "");
  assert.equal(created.repository, "");
  assert.equal(created.asset, "");
});

test("Add environment opens as a new dev environment right after editing one", async () => {
  const target = {
    slug: "foreverpin-prod",
    product: "foreverpin",
    server: "fra1",
    environment: "prod",
    network: "edge",
    root: "/srv/prod",
    settings: [],
    smokeChecks: [],
    sites: [{ site: "app", host: "foreverpin.example.com" }],
  };
  const servers = [{ slug: "hel1" }, { slug: "fra1" }];
  const { edited, created } = await editThenNew(
    dialogs.TargetFormModal,
    { product: "foreverpin", servers },
    "target",
    target,
  );
  assert.equal(edited.slug, "foreverpin-prod");
  assert.equal(created.slug, "foreverpin-dev");
  assert.equal(created.environment, "dev");
  assert.equal(created.server, "hel1");
  assert.equal(created.network, "platform");
  assert.deepEqual(created.sites, []);
});

test("the environment form prefills each known public site's baseline host and keeps a host the owner named", async () => {
  const servers = [{ slug: "ocharo-vps", ingress: { publicAddress: "203.0.113.10" } }];
  const knownSites = [{ name: "app", exposure: "public", primary: true }, { name: "admin", exposure: "private", primary: false }];
  const mounted = mountDialog(dialogs.TargetFormModal, { product: "ocharo-studio", servers, knownSites, target: null });
  mounted.state.open = true;
  await nextTick();
  assert.deepEqual(mounted.values().sites, [{ site: "app", host: "ocharo-studio-dev.203-0-113-10.sslip.io" }]);
  mounted.unmount();
  // Prod drops -dev; a host the owner typed stays put.
  const edited = mountDialog(dialogs.TargetFormModal, { product: "ocharo-studio", servers, knownSites, target: null });
  Object.assign(edited.state, { target: { slug: "ocharo-studio-prod", product: "ocharo-studio", server: "ocharo-vps",
    environment: "prod", network: "platform", root: "/srv/wheelhouse", settings: [], smokeChecks: [],
    sites: [{ site: "status", host: "status.ocharo.example" }] }, open: true });
  await nextTick();
  assert.deepEqual(edited.values().sites, [{ site: "status", host: "status.ocharo.example" },
    { site: "app", host: "ocharo-studio.203-0-113-10.sslip.io" }]);
  edited.unmount();
});

test("New server opens with the defaults right after editing a server", async () => {
  const server = {
    slug: "fra1",
    name: "Frankfurt",
    provider: "ovh",
    host: "vps.example.net",
    region: "fra1",
    sshUser: "ops",
    sshPort: 2222,
    ingress: {
      scheme: "http",
      port: 8080,
      entryPoints: ["web"],
      privateEntryPoints: [],
      certResolver: null,
      pattern: null,
      probe: null,
      privateProbe: null,
    },
  };
  const { edited, created } = await editThenNew(dialogs.ServerFormModal, {}, "server", server);
  assert.equal(edited.name, "Frankfurt");
  assert.equal(created.slug, "");
  assert.equal(created.name, "");
  assert.equal(created.provider, "hetzner");
  assert.equal(created.sshPort, 22);
});

test("Add vault opens named after its server right after editing a vault", async () => {
  const vault = { slug: "fra1-secrets", name: "Secrets", server: "fra1", url: "http://secrets:9000" };
  const { edited, created } = await editThenNew(dialogs.VaultFormModal, { server: "fra1" }, "vault", vault);
  assert.equal(edited.slug, "fra1-secrets");
  assert.equal(created.slug, "fra1-vault");
  assert.equal(created.name, "");
  assert.equal(created.url, "http://vault:8080");
});
