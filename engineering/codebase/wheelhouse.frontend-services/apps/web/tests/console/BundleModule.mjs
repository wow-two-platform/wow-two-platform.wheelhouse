import { resolve } from "node:path";
import { build } from "esbuild";

/**
 * Bundles framework-independent source modules in memory and imports them; `contents` is the export statement, with
 * paths relative to the app root and the `@` alias resolved. No browser, server or Vue renderer is involved.
 */
export async function bundleModule(contents) {
  const bundle = await build({
    stdin: { contents, resolveDir: process.cwd() },
    alias: { "@": resolve("src") },
    bundle: true,
    platform: "node",
    format: "esm",
    write: false,
  });
  return import(`data:text/javascript;base64,${Buffer.from(bundle.outputFiles[0].text).toString("base64")}`);
}
