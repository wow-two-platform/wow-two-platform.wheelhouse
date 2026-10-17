import assert from "node:assert/strict";
import { after, test } from "node:test";
import { resolve } from "node:path";
import { build, stop } from "esbuild";
import { target } from "./LoadWorkspaceModule.mjs";

// Bundle the real domain rules: the one environment order and the comparison that reads it.
const bundle = await build({
  stdin: {
    contents: `
      export { DeploymentEnvironment, EnvironmentOrder, environmentRank } from './src/domain/targets';
      export { compareEnvironments } from './src/domain/deployments';
    `,
    resolveDir: process.cwd(),
  },
  alias: { "@": resolve("src") },
  bundle: true,
  platform: "node",
  format: "esm",
  write: false,
});
const domain = await import(`data:text/javascript;base64,${Buffer.from(bundle.outputFiles[0].text).toString("base64")}`);
after(() => stop());

test("environments follow the promotion path dev, test, prod, never the alphabet", () => {
  assert.deepEqual(domain.EnvironmentOrder, ["dev", "test", "prod"]);
  assert.deepEqual(domain.EnvironmentOrder, Object.values(domain.DeploymentEnvironment));
  const sorted = ["prod", "rehearsal", "test", "dev"].sort((a, b) => domain.environmentRank(a) - domain.environmentRank(b));
  assert.deepEqual(sorted, ["dev", "test", "prod", "rehearsal"]);
});

test("the comparison lines environments up along the path whatever order the targets arrive in", () => {
  const targets = ["prod", "dev", "test"].map((environment) =>
    target({ id: `foreverpin-${environment}`, environment }),
  );
  const comparison = domain.compareEnvironments(targets, [undefined, undefined, undefined], []);
  assert.deepEqual(
    comparison.columns.map((column) => column.environment),
    ["dev", "test", "prod"],
  );
});
