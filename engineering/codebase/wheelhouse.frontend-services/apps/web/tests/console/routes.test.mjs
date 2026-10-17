import assert from "node:assert/strict";
import { after, test } from "node:test";
import { resolve } from "node:path";
import { build, stop } from "esbuild";

// Bundle the real route table in memory; pages are stand-ins, since only paths, redirects and handles are checked.
const bundle = await build({
  stdin: {
    contents: `export { router, HomePlace, PrimaryPlaces, GlobalPlaces, SettingsPlaces } from './src/bootstrap/routes';`,
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
const { router, HomePlace, PrimaryPlaces, GlobalPlaces, SettingsPlaces } = await import(
  `data:text/javascript;base64,${Buffer.from(bundle.outputFiles[0].text).toString("base64")}`
);
after(() => stop());

test("every place keeps its path and takes its title and description from the route handle", async () => {
  for (const place of [HomePlace, ...PrimaryPlaces, ...GlobalPlaces, ...SettingsPlaces]) {
    await router.push(place.path);
    assert.equal(router.currentRoute.value.path, place.path);
    assert.equal(router.currentRoute.value.meta.title, place.label);
    assert.equal(router.currentRoute.value.meta.description, place.description);
  }
});

test("the primary places are the entities, and secrets and activity stay addressable outside them", () => {
  assert.deepEqual(PrimaryPlaces.map((place) => place.path), ["/products", "/deployments", "/servers"]);
  assert.deepEqual(GlobalPlaces.map((place) => place.path), ["/activity", "/secrets"]);
});

test("the old fleet address and unknown addresses redirect with their query", async () => {
  await router.push("/fleet?view=hosts");
  assert.equal(router.currentRoute.value.fullPath, "/servers?view=hosts");

  await router.push("/no-such-page?view=hosts");
  assert.equal(router.currentRoute.value.fullPath, "/?view=hosts");
});

test("an old Workspace link opens the product's own page with its other parameters", async () => {
  await router.push("/?product=foreverpin&target=foreverpin-dev&inspect=service&item=api");
  assert.equal(router.currentRoute.value.path, "/p/foreverpin");
  assert.deepEqual(router.currentRoute.value.query, { target: "foreverpin-dev", inspect: "service", item: "api" });

  await router.push("/no-such-page?product=foreverpin");
  assert.equal(router.currentRoute.value.fullPath, "/p/foreverpin");

  await router.push("/?product=");
  assert.equal(router.currentRoute.value.path, "/");
});

test("workspace and product pages take their slug from the path and title from it", async () => {
  await router.push("/w/platform");
  assert.equal(router.currentRoute.value.params.workspace, "platform");
  assert.equal(router.currentRoute.value.meta.title(router.currentRoute.value), "platform");

  await router.push("/p/wheelhouse?env=prod&inspect=service&item=api");
  assert.equal(router.currentRoute.value.params.product, "wheelhouse");
  assert.deepEqual(router.currentRoute.value.query, { env: "prod", inspect: "service", item: "api" });
  assert.equal(router.currentRoute.value.meta.title(router.currentRoute.value), "wheelhouse");
  assert.equal(router.currentRoute.value.params.tab, "");
});

test("a product's tab is a path segment on the same route, and a workspace opens the products browser", async () => {
  await router.push("/p/wheelhouse/releases");
  const tabRecord = router.currentRoute.value.matched.at(-1);
  assert.deepEqual(router.currentRoute.value.params, { product: "wheelhouse", tab: "releases" });

  await router.push("/p/wheelhouse?env=prod");
  assert.equal(router.currentRoute.value.matched.at(-1), tabRecord, "Overview and the tabs share one route record.");

  await router.push("/w/platform");
  assert.equal(router.currentRoute.value.params.workspace, "platform");
  assert.equal(router.currentRoute.value.matched.at(-1).meta.description, "A workspace and its products.");
});
