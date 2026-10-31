import assert from "node:assert/strict";
import { after, afterEach, test } from "node:test";
import { stop } from "esbuild";
import { bundleModule } from "./BundleModule.mjs";

// Generated and linked keys in the console: the wire decoding, the editors' read-only linked key and Regenerate, the
// deploy dialog never asking for a linked or generated key, and the regenerate call.
const { EnvironmentSettingsRules, DeploySettingsRules, DeploySettingsSchema, SecretWrittenSchema, deploySettingsApi } =
  await bundleModule(`
    export { EnvironmentSettingsRules } from './src/domain/settings';
    export { DeploySettingsRules } from './src/domain/deployments';
    export { DeploySettingsSchema, SecretWrittenSchema } from './src/integration/deployments/settingsSchemas';
    export { deploySettingsApi } from './src/integration/deployments/settingsApi';
  `);
const originalFetch = globalThis.fetch;
afterEach(() => {
  globalThis.fetch = originalFetch;
});
after(() => stop());

const json = (data, status = 200) =>
  new Response(JSON.stringify(data), { status, headers: { "Content-Type": "application/json" } });
const key = (name, source, extra = {}) => ({
  key: name, required: false, secret: false, source, value: null, default: null, derived: null, allowed: null, ...extra,
});
const command = ["dotnet", "Identity.Api.dll", "keys", "generate", "--role", "signing"];

const plan = (firstDeploy = false) => DeploySettingsSchema.parse({
  target: "ocharo-dev", product: "ocharo", environment: "dev", release: "v0.3.6", firstDeploy, missing: 1,
  vault: { id: "built-in", namespace: "ocharo-dev", reachable: true },
  services: [
    {
      name: "desk", hostFile: false,
      keys: [
        key("Auth:ClientSecret", "missing", { required: true, secret: true, from: "Identity:Server:Clients:0:ClientSecret" }),
        key("Auth:Issuer", "linked", { required: true, from: "Sites:identity:Url" }),
        key("Desk:SupportEmail", "linked", { from: "Support:Email", value: "help@example.com", type: "email" }),
      ],
    },
    {
      name: "identity", hostFile: false,
      keys: [
        key("Identity:Server:Clients:0:ClientSecret", "generate", { required: true, secret: true,
          generate: { random: 32, encoding: "base64url" } }),
        key("Identity:Server:Keys:Signing:0", "vault", { required: true, secret: true, generate: { command, timeoutSeconds: 60 },
          vault: { key: "Identity:Server:Keys:Signing:0", version: 1, updatedAt: "2026-10-31T10:00:00Z", updatedBy: "max",
            fingerprint: "0123456789abcdef", expiresAt: "2027-03-04T05:06:07Z", expiry: null } }),
        key("Identity:Server:Issuer", "linked", { required: true, from: "Sites:identity:Url" }),
        key("Support:Email", "default", { default: "help@example.com", type: "email" }),
      ],
    },
  ],
});

test("a plan decodes how a key is generated and what a linked key takes its value from, and an older one without them", () => {
  const settings = plan();
  const [linked] = settings.services[0].keys;
  assert.deepEqual([linked.source, linked.from, linked.generate], ["missing", "Identity:Server:Clients:0:ClientSecret", null]);
  const [random, signing] = settings.services[1].keys;
  assert.deepEqual(random.generate, { random: 32, encoding: "base64url", command: null, timeoutSeconds: null });
  assert.deepEqual([signing.generate.command, signing.generate.timeoutSeconds, signing.generate.random], [command, 60, null]);
  const older = DeploySettingsSchema.parse({ ...settings, services: [{ name: "api", hostFile: false, keys: [key("A:B", "stored")] }] });
  assert.deepEqual([older.services[0].keys[0].generate, older.services[0].keys[0].from], [null, null]);
});

test("the editors list a linked key read-only with its source, apart from the keys they edit", () => {
  const settings = plan();
  const plain = EnvironmentSettingsRules.settings(settings);
  const email = plain.find((row) => row.key === "Desk:SupportEmail");
  assert.equal(EnvironmentSettingsRules.isLinked(email), true);
  assert.equal(EnvironmentSettingsRules.sourceLabel(email), "from Support:Email");
  assert.equal(email.value, "help@example.com", "a plain source's value shows through");
  assert.equal(EnvironmentSettingsRules.isLinked(plain.find((row) => row.key === "Support:Email")), false);

  const secrets = EnvironmentSettingsRules.secrets(settings);
  const linked = secrets.find((row) => row.key === "Auth:ClientSecret");
  assert.deepEqual([EnvironmentSettingsRules.isLinked(linked), EnvironmentSettingsRules.canRegenerate(linked)], [true, false]);
  assert.equal(EnvironmentSettingsRules.sourceLabel(linked), "from Identity:Server:Clients:0:ClientSecret, not set yet");
  assert.equal(linked.value, null, "a secret's value never comes to the browser");
});

test("a generated secret offers Regenerate with how it is made, and reads as generated before its first deploy", () => {
  const secrets = EnvironmentSettingsRules.secrets(plan());
  const random = secrets.find((row) => row.key === "Identity:Server:Clients:0:ClientSecret");
  const signing = secrets.find((row) => row.key === "Identity:Server:Keys:Signing:0");
  assert.deepEqual([EnvironmentSettingsRules.canRegenerate(random), EnvironmentSettingsRules.canRegenerate(signing)], [true, true]);
  assert.equal(EnvironmentSettingsRules.sourceLabel(random), "generated at the next deploy");
  assert.equal(EnvironmentSettingsRules.generateLabel(random.generate), "32 random bytes, base64url");
  assert.equal(EnvironmentSettingsRules.generateLabel(signing.generate), "command: dotnet Identity.Api.dll keys generate --role signing");
  assert.equal(EnvironmentSettingsRules.generateLabel(null), null);
  const typed = secrets.find((row) => row.key === "Identity:Server:Keys:Signing:0");
  assert.equal(EnvironmentSettingsRules.canRegenerate({ ...typed, generate: null }), false, "a typed secret is written, not regenerated");
});

test("the deploy dialog never asks for a linked or generated key, and names both instead", () => {
  const settings = plan(true);
  const asked = DeploySettingsRules.prompted(settings);
  assert.deepEqual(asked.map((item) => item.key), ["Support:Email"],
    "the missing linked key waits on its generated source; only an optional key of its own is offered");
  assert.deepEqual(DeploySettingsRules.generated(settings), ["Identity:Server:Clients:0:ClientSecret"]);
  assert.deepEqual(DeploySettingsRules.linked(settings).map((item) => [item.key, item.from, item.value, item.secret]), [
    ["Auth:ClientSecret", "Identity:Server:Clients:0:ClientSecret", null, true],
    ["Auth:Issuer", "Sites:identity:Url", null, false],
    ["Desk:SupportEmail", "Support:Email", "help@example.com", false],
    ["Identity:Server:Issuer", "Sites:identity:Url", null, false],
  ]);
  assert.deepEqual(DeploySettingsRules.held(settings).map((item) => item.key), ["Identity:Server:Keys:Signing:0"]);
});

test("Regenerate posts with the settings action and reads the new version, never a value", async () => {
  const requests = [];
  globalThis.fetch = async (url, options) => {
    requests.push([url, options.method, options.headers.get("X-Wheelhouse-Action"), JSON.parse(options.body)]);
    return json({ data: { key: "Identity:Server:Clients:0:ClientSecret", version: 2, fingerprint: "fedcba9876543210" } });
  };
  const result = await deploySettingsApi.regenerate("ocharo-dev", "Identity:Server:Clients:0:ClientSecret", null);
  assert.deepEqual([result.ok, result.value.version, result.value.fingerprint], [true, 2, "fedcba9876543210"]);
  await deploySettingsApi.regenerate("ocharo-dev", "Identity:Server:Keys:Signing:0", "rotated");
  assert.deepEqual(requests, [
    ["/api/deployments/targets/ocharo-dev/secrets/Identity%3AServer%3AClients%3A0%3AClientSecret/regenerate", "POST", "settings", {}],
    ["/api/deployments/targets/ocharo-dev/secrets/Identity%3AServer%3AKeys%3ASigning%3A0/regenerate", "POST", "settings", { note: "rotated" }],
  ]);
  assert.deepEqual(Object.keys(SecretWrittenSchema.parse({ key: "A:B", version: 1, fingerprint: "ab", value: "LEAK" })).sort(),
    ["fingerprint", "key", "version"], "a stray value in a reply is dropped on decoding");
});

test("a failed regeneration reads as the server's reason", async () => {
  globalThis.fetch = async () => json({ title: "Conflict", status: 409,
    detail: "Generating Identity:Server:Keys:Signing:0 failed: keys generate exited 2: unknown role" }, 409);
  const result = await deploySettingsApi.regenerate("ocharo-dev", "Identity:Server:Keys:Signing:0", null);
  assert.equal(result.ok, false);
  assert.equal(result.failure.status, 409);
});
