import assert from "node:assert/strict";
import { after, test } from "node:test";
import { resolve } from "node:path";
import { build, stop } from "esbuild";

// Bundle the real icon modules, with the Vue copy they share, in memory.
const bundle = await build({
  stdin: {
    contents: `
      export * from './src/domain/products/ProductIcon';
      export { useProductIcon } from './src/application/products/useProductIcon';
      export { ref } from 'vue';
    `,
    resolveDir: process.cwd(),
  },
  alias: { "@": resolve("src") },
  bundle: true,
  platform: "node",
  format: "esm",
  write: false,
});
const icons = await import(
  `data:text/javascript;base64,${Buffer.from(bundle.outputFiles[0].text).toString("base64")}`
);
after(() => stop());

const light = "/api/products/wheelhouse/icon?theme=light&form=short&color=colored";
const dark = "/api/products/wheelhouse/icon?theme=dark&form=short&color=colored";

test("an icon address names the theme, form and color, and a service when one is asked for", () => {
  assert.equal(icons.productIconUrl("wheelhouse"), light);
  assert.equal(icons.productIconUrl("wheelhouse", { theme: "dark" }), dark);
  assert.equal(
    icons.productIconUrl("foreverpin", { service: "redirect", theme: "dark", form: "full", color: "monochrome" }),
    "/api/products/foreverpin/services/redirect/icon?theme=dark&form=full&color=monochrome",
  );
  assert.equal(icons.productIconUrl("foreverpin", { service: null }), "/api/products/foreverpin/icon?theme=light&form=short&color=colored");
  assert.equal(icons.productIconUrl("a/b c"), "/api/products/a%2Fb%20c/icon?theme=light&form=short&color=colored");
});

test("the monogram is the name's first letter and its tint is stable for a name", () => {
  assert.equal(icons.monogramOf("  forever pin"), "F");
  assert.equal(icons.monogramOf("   "), "?");
  assert.equal(icons.monogramHue("Wheelhouse"), icons.monogramHue("Wheelhouse"));
  assert.notEqual(icons.monogramHue("Wheelhouse"), icons.monogramHue("ForeverPin"));
  assert.ok(icons.monogramHue("Wheelhouse") >= 0 && icons.monogramHue("Wheelhouse") < 360);
});

test("a product's icon asks for the variant of the active theme and asks again when the theme changes", () => {
  // The SDK colour-mode provider owns the scheme; the icon reads its mode through this getter.
  const theme = icons.ref("light");
  const icon = icons.useProductIcon({ slug: () => "wheelhouse", name: () => "Wheelhouse", theme: () => theme.value });
  assert.equal(icon.src.value, light);

  theme.value = "dark";
  assert.equal(icon.src.value, dark, "the dark console must not keep the light icon");

  theme.value = "light";
  assert.equal(icon.src.value, light);
});

test("a refused address shows the monogram until the theme, service or product changes", () => {
  const theme = icons.ref("light");
  const service = icons.ref(null);
  const slug = icons.ref("wheelhouse");
  const icon = icons.useProductIcon({
    slug: () => slug.value,
    service: () => service.value,
    name: () => "Wheelhouse",
    theme: () => theme.value,
  });
  assert.equal(icon.src.value, light);

  icon.failed();
  assert.equal(icon.src.value, null, "a refused address shows the monogram");
  assert.equal(icon.monogram.value, "W");

  theme.value = "dark";
  assert.equal(icon.src.value, dark, "a new theme is a new attempt");

  icon.failed();
  service.value = "console";
  assert.equal(icon.src.value, "/api/products/wheelhouse/services/console/icon?theme=dark&form=short&color=colored");

  icon.failed();
  slug.value = "foreverpin";
  assert.equal(icon.src.value, "/api/products/foreverpin/services/console/icon?theme=dark&form=short&color=colored");
});

test("the form and color a surface asks for reach the address", () => {
  const icon = icons.useProductIcon({
    slug: () => "foreverpin",
    name: () => "ForeverPin",
    theme: () => "dark",
    form: () => "full",
    color: () => "monochrome",
  });

  assert.equal(icon.src.value, "/api/products/foreverpin/icon?theme=dark&form=full&color=monochrome");
});

test("a product outside the catalog shows its monogram without asking for an icon", () => {
  const icon = icons.useProductIcon({ slug: () => null, name: () => "unknown tool", theme: () => "dark" });

  assert.equal(icon.src.value, null);
  assert.equal(icon.monogram.value, "U");
});
