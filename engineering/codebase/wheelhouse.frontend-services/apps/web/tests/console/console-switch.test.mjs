import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { resolve } from "node:path";
import { after, afterEach, test } from "node:test";
import { build, stop } from "esbuild";
import { compileScript, parse } from "@vue/compiler-sfc";
import { createSSRApp } from "vue";
import { renderToString } from "vue/server-renderer";
import { bundleModule } from "./BundleModule.mjs";

// The environment tag's switch: the address rules and the owner's read of the other console, bundled from the real
// modules, then the real menu component rendered to markup.
const { ConsolePeerRules, systemApi } = await bundleModule(`
  export { ConsolePeerRules } from './src/domain/system';
  export { systemApi } from './src/integration/system';
`);

const originalFetch = globalThis.fetch;
afterEach(() => {
  globalThis.fetch = originalFetch;
});

function json(data, status = 200) {
  return new Response(JSON.stringify(data), { status, headers: { "Content-Type": "application/json" } });
}

const Peer = "https://wheelhouse-peer.example.test:8443";

test("opens the same page on the peer: path, query and hash all kept", () => {
  assert.equal(
    ConsolePeerRules.hrefOf(Peer, "/p/foreverpin/deployments?view=list&release=v1.2.3&env=prod#row-4"),
    `${Peer}/p/foreverpin/deployments?view=list&release=v1.2.3&env=prod#row-4`,
  );
  assert.equal(ConsolePeerRules.hrefOf(Peer, "/deployments"), `${Peer}/deployments`);
  assert.equal(ConsolePeerRules.hrefOf(Peer, "/"), `${Peer}/`);
  assert.equal(ConsolePeerRules.hrefOf(Peer, "/products#workspace-ventures"), `${Peer}/products#workspace-ventures`);
});

test("a trailing slash on the peer, or none on the path, never doubles or drops a slash", () => {
  assert.equal(ConsolePeerRules.hrefOf(`${Peer}/`, "/servers?x=1"), `${Peer}/servers?x=1`);
  assert.equal(ConsolePeerRules.hrefOf(Peer, "servers"), `${Peer}/servers`);
  assert.equal(ConsolePeerRules.hrefOf(Peer, ""), `${Peer}/`);
});

test("a path can never name another host", () => {
  for (const path of ["//evil.example/x", "/\\evil.example/x", "///evil.example", "/@evil.example"]) {
    const href = new URL(ConsolePeerRules.hrefOf(Peer, path));
    assert.equal(href.origin, Peer, `${path} stays on the peer`);
  }
});

test("only an https origin is a console to switch to", () => {
  assert.equal(ConsolePeerRules.isOrigin("https://wheelhouse-peer.example.test"), true);
  assert.equal(ConsolePeerRules.isOrigin(Peer), true);
  assert.equal(ConsolePeerRules.isOrigin(`${Peer}/`), true);
  for (const value of [
    "",
    "http://wheelhouse-peer.example.test",
    "wheelhouse-peer.example.test",
    "javascript:alert(1)",
    "https://owner:secret@wheelhouse-peer.example.test",
    `${Peer}/p/foreverpin`,
    `${Peer}?next=1`,
    `${Peer}#top`,
  ]) {
    assert.equal(ConsolePeerRules.isOrigin(value), false, JSON.stringify(value));
  }
});

test("the tag stays as it is without a usable peer", () => {
  assert.equal(ConsolePeerRules.from(null), null);
  assert.equal(ConsolePeerRules.from(undefined), null);
  assert.equal(ConsolePeerRules.from({ url: null, environment: null }), null, "an unset host reads empty");
  assert.equal(ConsolePeerRules.from({ url: Peer, environment: null }), null);
  assert.equal(ConsolePeerRules.from({ url: null, environment: "prod" }), null);
  assert.equal(ConsolePeerRules.from({ url: "http://wheelhouse-peer.example.test", environment: "prod" }), null);
  assert.deepEqual(ConsolePeerRules.from({ url: `${Peer}/`, environment: "prod" }), { url: Peer, environment: "prod" });
});

test("reads the other console from the owner's endpoint, and an unset host as empty", async () => {
  const requests = [];
  const answers = [{ data: { url: Peer, environment: "prod" } }, { data: { url: null, environment: null } }, { data: {} }];
  globalThis.fetch = async (url, options) => {
    requests.push({ url, credentials: options.credentials });
    return json(answers[requests.length - 1]);
  };

  assert.deepEqual((await systemApi.getPeer()).value, { url: Peer, environment: "prod" });
  assert.deepEqual((await systemApi.getPeer()).value, { url: null, environment: null });
  assert.deepEqual((await systemApi.getPeer()).value, { url: null, environment: null }, "absent fields read as unset");
  assert.deepEqual(requests.map(({ url }) => url), Array(3).fill("/api/system/peer"));
  assert.equal(requests[0].credentials, "same-origin");
});

test("refuses a reading that names an unknown environment, and a refused read gives no peer", async () => {
  globalThis.fetch = async () => json({ data: { url: Peer, environment: "staging" } });
  assert.equal((await systemApi.getPeer()).ok, false);

  globalThis.fetch = async () => new Response(null, { status: 403 });
  assert.equal((await systemApi.getPeer()).ok, false);
});

// The menu, compiled from the real single-file component (script, template and all) and rendered to markup; packages
// stay external so the component and this harness share one Vue.
const bundle = await build({
  stdin: {
    contents: `export { default as Menu } from './src/presentation/shell/components/EnvironmentSwitchMenu.vue';`,
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
      name: "sfc",
      setup(build) {
        build.onLoad({ filter: /\.vue$/ }, async (args) => {
          const { descriptor } = parse(await readFile(args.path, "utf8"), { filename: args.path });
          return { contents: compileScript(descriptor, { id: args.path, inlineTemplate: true }).content, loader: "ts" };
        });
      },
    },
  ],
});
const { Menu } = await import(
  `data:text/javascript;base64,${Buffer.from(
    bundle.outputFiles[0].text.replace(
      /(from\s+["'])([^"'./][^"']*)(["'])/g,
      (_match, before, name, end) => `${before}${import.meta.resolve(name)}${end}`,
    ),
  ).toString("base64")}`
);
after(() => stop());

const render = (props) => renderToString(createSSRApp(Menu, props));

test("the menu offers one plain link to the same page on the other console, with the page it opens and a hint", async () => {
  const html = await render({
    environment: "dev",
    peer: { url: Peer, environment: "prod" },
    fullPath: "/p/foreverpin/deployments?view=list&release=v1.2.3#row-4",
    version: "v0.5.13",
  });

  assert.equal((html.match(/<a /g) ?? []).length, 1, "one link, nothing else to press");
  assert.match(html, /<a href="https:\/\/wheelhouse-peer\.example\.test:8443\/p\/foreverpin\/deployments\?view=list&amp;release=v1\.2\.3#row-4"/);
  assert.match(html, /Open prod/);
  assert.match(html, />\/p\/foreverpin\/deployments\?view=list&amp;release=v1\.2\.3#row-4</, "the page it opens");
  assert.match(html, /dev console · v0\.5\.13/);
  assert.match(html, /Same page and filters, on the prod console\./);
  assert.doesNotMatch(html, /target=/, "it opens here; Cmd or Ctrl click takes a new tab");
  assert.doesNotMatch(html, /data-console-switch[^>]*onclick/);
});

test("the prod console offers dev", async () => {
  const html = await render({
    environment: "prod",
    peer: { url: Peer, environment: "dev" },
    fullPath: "/",
    version: "v0.3.9",
  });

  assert.match(html, /Open dev/);
  assert.match(html, /prod console · v0\.3\.9/);
  assert.match(html, /<a href="https:\/\/wheelhouse-peer\.example\.test:8443\/"/);
});
