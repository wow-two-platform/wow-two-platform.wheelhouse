import assert from "node:assert/strict";
import { after, test } from "node:test";
import { stop } from "esbuild";
import { bundleModule } from "./BundleModule.mjs";

// The deploy dialog's settings step: what it asks for, what blocks the deploy and what it saves, and the wire decoding.
const settings = await bundleModule(`
  export { DeploySettingsRules } from './src/domain/deployments';
  export { DeploySettingsSchema } from './src/integration/deployments/settingsSchemas';
`);
after(() => stop());
const { DeploySettingsRules, DeploySettingsSchema } = settings;

const key = (name, source, extra = {}) => ({
  key: name, required: false, secret: false, source, value: null, default: null, derived: null, allowed: null, ...extra,
});

const plan = (firstDeploy, hostFile = false) => DeploySettingsSchema.parse({
  target: "ocharo-platform-dev", product: "ocharo-platform", environment: "dev", release: "v0.1.0", firstDeploy,
  vault: { id: "control-vault", namespace: "ocharo-platform-dev", reachable: true },
  missing: 2,
  services: [
    {
      name: "desk", hostFile,
      keys: [
        key("AllowedHosts", "derived", { required: true, derived: "hosts", value: "desk.example;ocharo-platform-dev-desk;localhost" }),
        key("ConnectionStrings:Database", "derived", { required: true, secret: true, derived: "database" }),
        key("DataProtection:ApplicationName", "missing", { required: true }),
        key("DataProtection:KeysPath", "stored", { required: true, value: "/var/lib/keys" }),
        key("Feature:Flag", "default", { default: "false", allowed: ["true", "false"] }),
        key("Sites:app:Url", "derived", { derived: "sites", value: "https://desk.example" }),
      ],
    },
    {
      name: "image-worker", hostFile: false,
      keys: [
        key("ImageWorker:Key", "missing", { required: true, secret: true }),
        key("Feature:Flag", "unset"),
      ],
    },
  ],
});

test("the dialog asks for every required key no layer sets, required first, once per key", () => {
  const prompted = DeploySettingsRules.prompted(plan(false));
  assert.deepEqual(prompted.map((item) => [item.key, item.required, item.secret, item.services]), [
    ["DataProtection:ApplicationName", true, false, ["desk"]],
    ["ImageWorker:Key", true, true, ["image-worker"]],
  ]);
});

test("a first deploy also offers the optional keys, with their defaults and choices", () => {
  const prompted = DeploySettingsRules.prompted(plan(true));
  const flag = prompted.find((item) => item.key === "Feature:Flag");
  assert.deepEqual([flag.required, flag.default, flag.allowed, flag.services], [false, "false", ["true", "false"], ["desk", "image-worker"]]);
  assert.deepEqual(prompted.map((item) => item.key), ["DataProtection:ApplicationName", "ImageWorker:Key", "Feature:Flag"]);
});

test("a service with a host file leaves its required keys to the target", () => {
  const prompted = DeploySettingsRules.prompted(plan(false, true));
  assert.deepEqual(prompted.map((item) => item.key), ["ImageWorker:Key"]);
});

test("required keys block until filled, and only filled entries save, as secrets where kept so", () => {
  const prompted = DeploySettingsRules.prompted(plan(true));
  assert.deepEqual(DeploySettingsRules.blocking(prompted, {}), ["DataProtection:ApplicationName", "ImageWorker:Key"]);
  const entered = { "DataProtection:ApplicationName": "ocharo-desk", "ImageWorker:Key": "  ", "Feature:Flag": "" };
  assert.deepEqual(DeploySettingsRules.blocking(prompted, entered), ["ImageWorker:Key"]);
  entered["ImageWorker:Key"] = "s3cr3t";
  assert.deepEqual(DeploySettingsRules.blocking(prompted, entered), []);
  assert.deepEqual(DeploySettingsRules.values(prompted, entered, { "DataProtection:ApplicationName": true }), [
    { key: "DataProtection:ApplicationName", value: "ocharo-desk", secret: true },
    { key: "ImageWorker:Key", value: "s3cr3t", secret: true },
  ]);
});

test("the derived keys are named once each with where they come from", () => {
  const derived = DeploySettingsRules.derived(plan(false));
  assert.deepEqual(derived.map((item) => [item.key, DeploySettingsRules.describe(item)]), [
    ["AllowedHosts", "desk.example;ocharo-platform-dev-desk;localhost"],
    ["ConnectionStrings:Database", "the target's own database"],
    ["Sites:app:Url", "https://desk.example"],
  ]);
});

test("the plan decodes without a value for a secret and refuses an unknown source", () => {
  const decoded = plan(false);
  assert.equal(decoded.services[1].keys[0].value, null);
  assert.equal(decoded.vault.reachable, true);
  assert.throws(() => DeploySettingsSchema.parse({ ...decoded, services: [{ name: "x", hostFile: false, keys: [key("A", "guessed")] }] }));
});
