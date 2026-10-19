import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { test } from "node:test";
import { transform } from "esbuild";
import { compileScript, compileTemplate, parse, rewriteDefault } from "@vue/compiler-sfc";
import { createSSRApp } from "vue";
import { renderToString } from "vue/server-renderer";

// An attention panel renders only while it has items: a healthy portfolio shows no "Needs attention" box, and a
// reading still on its way never flashes an empty one. The real panels are compiled for server rendering over a
// stubbed attention source and router; packages resolve to the installed copies.

const asUrl = (source) => `data:text/javascript;base64,${Buffer.from(source).toString("base64")}`;

const vue = import.meta.resolve("vue");
const stubs = {
  "@/application/overview": asUrl(`
    import { ref } from ${JSON.stringify(vue)};
    export function usePortfolioAttention() {
      const { items, incomplete } = globalThis.__attention;
      return { items: ref(items), incomplete: ref(incomplete), loading: ref(false) };
    }
  `),
  "vue-router": asUrl(`
    import { defineComponent, h } from ${JSON.stringify(vue)};
    export const RouterLink = defineComponent({
      props: ["to"],
      setup: (props, { slots }) => () => h("a", { href: typeof props.to === "string" ? props.to : props.to.path }, slots.default?.()),
    });
    export const useRoute = () => ({ query: {} });
  `),
};

/** Compiles one real panel for server rendering and imports it against the stubs. */
async function loadPanel(relativePath) {
  const filename = new URL(`../../${relativePath}`, import.meta.url).pathname;
  const { descriptor } = parse(await readFile(filename, "utf8"), { filename });
  const script = compileScript(descriptor, { id: filename });
  const template = compileTemplate({
    source: descriptor.template.content,
    filename,
    id: filename,
    ssr: true,
    ssrCssVars: [],
    compilerOptions: { bindingMetadata: script.bindings },
  });
  const source = `${rewriteDefault(script.content, "__panel")}\n${template.code}\n__panel.ssrRender = ssrRender;\nexport default __panel;`;
  const { code } = await transform(source, { loader: "ts", format: "esm" });
  const resolved = code.replace(/(from\s+["'])([^"']+)(["'])/g, (match, open, name, close) => {
    if (name in stubs) return `${open}${stubs[name]}${close}`;
    return name.startsWith(".") ? match : `${open}${import.meta.resolve(name)}${close}`;
  });
  return (await import(asUrl(resolved))).default;
}

const item = (id, tone = "danger") => ({ id, tone, title: `Title ${id}`, detail: `Detail ${id}`, href: `/servers/${id}` });

const panels = [
  ["the Servers page's PortfolioAttention", "src/presentation/servers/components/PortfolioAttention.vue"],
  ["the Home page's AttentionSummary", "src/presentation/home/components/AttentionSummary.vue"],
];

for (const [name, path] of panels) {
  test(`${name} renders nothing while no item needs attention`, async () => {
    const panel = await loadPanel(path);
    globalThis.__attention = { items: [], incomplete: false };
    const html = await renderToString(createSSRApp(panel));
    assert.doesNotMatch(html, /Needs attention/);
    assert.doesNotMatch(html, /No issues in the available/);
    assert.doesNotMatch(html, /<section/);
  });

  test(`${name} lists each item once something needs attention`, async () => {
    const panel = await loadPanel(path);
    globalThis.__attention = { items: [item("a"), item("b", "warning")], incomplete: false };
    const html = await renderToString(createSSRApp(panel));
    assert.match(html, /Needs attention/);
    assert.match(html, /Title a/);
    assert.match(html, /Detail b/);
  });
}

test("the Servers page's panel says the list may be incomplete only beside items", async () => {
  const panel = await loadPanel(panels[0][1]);
  globalThis.__attention = { items: [item("a")], incomplete: true };
  assert.match(await renderToString(createSSRApp(panel)), /may be incomplete/);
  globalThis.__attention = { items: [], incomplete: true };
  assert.doesNotMatch(await renderToString(createSSRApp(panel)), /Needs attention|may be incomplete/);
});
