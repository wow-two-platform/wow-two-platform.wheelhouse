import assert from "node:assert/strict";
import { after, test } from "node:test";
import { stop } from "esbuild";
import { bundleModule } from "./BundleModule.mjs";
import { product } from "./LoadModule.mjs";

const { secretBindingsOf, secretEnvironmentOf } = await bundleModule(`
  export { secretBindingsOf, secretEnvironmentOf } from './src/application/product/ProductSecrets';
`);
after(() => stop());

const environment = (name, vault) => ({ name, sites: [], secrets: vault ? { vault, namespace: `foreverpin-${name}` } : null });

test("a product's secrets are one namespace per environment, in promotion order", () => {
  const bindings = secretBindingsOf(product({ environments: [environment("prod", "ovh-vault"), environment("dev", null)] }));
  assert.deepEqual(bindings, [
    { environment: "dev", vault: null, namespace: null },
    { environment: "prod", vault: "ovh-vault", namespace: "foreverpin-prod" },
  ]);
  assert.deepEqual(secretBindingsOf(null), []);
});

test("the Secrets tab opens the addressed environment, else the remembered one, else the first with a vault", () => {
  const bindings = secretBindingsOf(product({ environments: [environment("dev", null), environment("prod", "ovh-vault")] }));
  assert.equal(secretEnvironmentOf(bindings, "dev", "prod").environment, "dev");
  assert.equal(secretEnvironmentOf(bindings, "staging", "dev").environment, "dev");
  assert.equal(secretEnvironmentOf(bindings, null, null).environment, "prod");
  assert.equal(secretEnvironmentOf([], "dev", "dev"), null);
});
