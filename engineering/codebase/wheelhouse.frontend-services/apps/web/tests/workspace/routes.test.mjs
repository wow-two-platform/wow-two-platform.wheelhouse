import assert from "node:assert/strict";
import { after, test } from "node:test";
import { resolve } from "node:path";
import { build, stop } from "esbuild";

// Bundle the real route table in memory; pages are stand-ins, since only paths, redirects and handles are checked.
const bundle = await build({
  stdin: {
    contents: `export { router, AppPlaces, SettingsPlaces } from './src/bootstrap/routes';`,
    resolveDir: process.cwd(),
  },
  alias: { "@": resolve("src") },
  bundle: true,
  platform: "node",
  format: "esm",
  write: false,
  plugins: [
    {
      name: "page-stand-ins",
      setup(build) {
        build.onResolve({ filter: /\.vue$/ }, (args) => ({ path: args.path, namespace: "page" }));
        build.onLoad({ filter: /.*/, namespace: "page" }, () => ({
          contents: "export default { render: () => null };",
          loader: "js",
        }));
      },
    },
  ],
});
const { router, AppPlaces, SettingsPlaces } = await import(
  `data:text/javascript;base64,${Buffer.from(bundle.outputFiles[0].text).toString("base64")}`
);
after(() => stop());

test("every place keeps its path and takes its title and description from the route handle", async () => {
  for (const place of [...AppPlaces, ...SettingsPlaces]) {
    await router.push(place.path);
    assert.equal(router.currentRoute.value.path, place.path);
    assert.equal(router.currentRoute.value.meta.title, place.label);
    assert.equal(router.currentRoute.value.meta.description, place.description);
  }
});

test("the old fleet address and unknown addresses redirect with their query", async () => {
  await router.push("/fleet?product=foreverpin&target=dev");
  assert.equal(router.currentRoute.value.fullPath, "/servers?product=foreverpin&target=dev");

  await router.push("/no-such-page?product=foreverpin");
  assert.equal(router.currentRoute.value.fullPath, "/?product=foreverpin");
});
