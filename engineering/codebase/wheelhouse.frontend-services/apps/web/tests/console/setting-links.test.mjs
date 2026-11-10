import assert from "node:assert/strict";
import { after, afterEach, test } from "node:test";
import { stop } from "esbuild";
import { bundleModule } from "./BundleModule.mjs";

// A key linked to another product's key in the same environment: the wire decoding, the editors' read-only linked key
// with its source and version, who may link and to which targets, the source keys offered, the deploy dialog naming the
// link and never asking for it, and the link and unlink calls.
const { EnvironmentSettingsRules, DeploySettingsRules, DeploySettingsSchema, SettingLinkSchema, deploySettingsApi } =
  await bundleModule(`
    export { EnvironmentSettingsRules } from './src/domain/settings';
    export { DeploySettingsRules } from './src/domain/deployments';
    export { DeploySettingsSchema, SettingLinkSchema } from './src/integration/deployments/settingsSchemas';
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
const sourceKey = "Identity:Server:Clients:8:ClientSecret";
const vault = { key: sourceKey, version: 3, updatedAt: "2026-11-10T10:00:00Z", updatedBy: "max", fingerprint: "0123456789abcdef" };

const plan = (overrides = {}) => DeploySettingsSchema.parse({
  target: "events-dev", product: "events", environment: "dev", release: "v0.1.0", firstDeploy: true, missing: 1,
  vault: { id: "built-in", namespace: "events-dev", reachable: true },
  services: [
    {
      name: "events", hostFile: false,
      keys: [
        key("Scfd:ClientSecret", "linked", { required: true, secret: true, vault, link: { target: "ocharo-dev", key: sourceKey } }),
        key("Scfd:BaseUrl", "missing", { required: true, type: "url", link: { target: "ocharo-dev", key: "Scfd:PublicUrl" } }),
        key("Events:Name", "missing", { required: true }),
        key("Auth:Issuer", "linked", { required: true, from: "Sites:identity:Url" }),
        key("AllowedHosts", "derived", { required: true, derived: "hosts", value: "events.example" }),
      ],
    },
  ],
  extra: [key("Scfd:TenantId", "linked", { value: "tenant-7", link: { target: "ocharo-dev", key: "Scfd:Tenants:Events" } })],
  ...overrides,
});

test("a plan decodes a key's link to another product's key, and an older one without it", () => {
  const settings = plan();
  const [secret] = settings.services[0].keys;
  assert.deepEqual([secret.source, secret.link, secret.vault.version], ["linked", { target: "ocharo-dev", key: sourceKey }, 3]);
  assert.equal(settings.services[0].keys[2].link, null);
  assert.deepEqual(Object.keys(SettingLinkSchema.parse({ target: "ocharo-dev", key: sourceKey, value: "LEAK" })).sort(), ["key", "target"],
    "a stray value in a reply is dropped on decoding");
});

test("the editors show a linked key read-only with its source and the source's version, never a value", () => {
  const settings = plan();
  const secret = EnvironmentSettingsRules.secrets(settings).find((row) => row.key === "Scfd:ClientSecret");
  assert.deepEqual([EnvironmentSettingsRules.isLinked(secret), EnvironmentSettingsRules.canRegenerate({ ...secret, generate: { random: 32 } })],
    [true, false]);
  assert.equal(EnvironmentSettingsRules.sourceLabel(secret), `linked to ocharo-dev · ${sourceKey}`);
  assert.deepEqual([secret.vault.version, secret.value], [3, null]);
  const url = EnvironmentSettingsRules.settings(settings).find((row) => row.key === "Scfd:BaseUrl");
  assert.equal(EnvironmentSettingsRules.sourceLabel(url), "linked to ocharo-dev · Scfd:PublicUrl, not set yet");
  const tenant = EnvironmentSettingsRules.settings(settings).find((row) => row.key === "Scfd:TenantId");
  assert.deepEqual([EnvironmentSettingsRules.isLinked(tenant), tenant.value], [true, "tenant-7"], "a plain source's value shows through");
});

test("a key the owner may link is neither derived nor linked already", () => {
  const rows = EnvironmentSettingsRules.keys(plan());
  const linkable = rows.filter((row) => EnvironmentSettingsRules.canLink(row)).map((row) => row.key);
  assert.deepEqual(linkable, ["Events:Name"]);
  assert.equal(EnvironmentSettingsRules.isDerived("Sites:app:Url"), true);
  assert.equal(EnvironmentSettingsRules.isDerived("deployment:trustedproxies"), true);
  assert.equal(EnvironmentSettingsRules.isDerived("Sites:app:Name"), false);
});

test("a link picks a target of another product in the same environment, and a key its source may give", () => {
  const targets = [
    { id: "ocharo-dev", product: "ocharo", environment: "dev" },
    { id: "files-dev", product: "files", environment: "dev" },
    { id: "ocharo-prod", product: "ocharo", environment: "prod" },
    { id: "events-dev", product: "events", environment: "dev" },
  ];
  assert.deepEqual(EnvironmentSettingsRules.linkTargets(targets, plan()), ["files-dev", "ocharo-dev"]);
  assert.deepEqual(EnvironmentSettingsRules.linkTargets(null, plan()), []);

  const source = DeploySettingsSchema.parse({
    target: "ocharo-dev", product: "ocharo", environment: "dev", release: "v0.3.6", firstDeploy: false, missing: 0,
    services: [{
      name: "identity", hostFile: false,
      keys: [
        key("Identity:Server:Issuer", "linked", { from: "Sites:identity:Url" }),
        key(sourceKey, "vault", { secret: true, vault }),
        key("Identity:Server:Clients:8:ClientId", "stored", { value: "events" }),
        key("Sites:identity:Url", "derived", { derived: "sites" }),
      ],
    }],
    extra: [key("Desk:EventsKey", "linked", { secret: true, link: { target: "events-dev", key: "Events:ApiKey" } })],
  });
  assert.deepEqual(EnvironmentSettingsRules.sourceKeys(source, "").map((row) => row.key),
    [sourceKey, "Identity:Server:Clients:8:ClientId"], "secrets first; derived and linked keys are no source, since links go one hop");
  assert.deepEqual(EnvironmentSettingsRules.sourceKeys(source, "clientid").map((row) => row.key), ["Identity:Server:Clients:8:ClientId"]);
  assert.deepEqual(EnvironmentSettingsRules.sourceKeys(null, ""), []);
});

test("a link names its target and a source key that is a configuration path, never a derived one", () => {
  assert.equal(EnvironmentSettingsRules.linkProblem(null, sourceKey), "Pick the target whose key it takes.");
  assert.equal(EnvironmentSettingsRules.linkProblem("ocharo-dev", " "), "Name the source key.");
  assert.equal(EnvironmentSettingsRules.linkProblem("ocharo-dev", "bad key"), "A source key is a configuration path such as Section:Key.");
  assert.match(EnvironmentSettingsRules.linkProblem("ocharo-dev", "AllowedHosts"), /derived at each deploy/);
  assert.equal(EnvironmentSettingsRules.linkProblem("ocharo-dev", sourceKey), null);
});

test("the deploy dialog never asks for a linked key, and names each link with its source target", () => {
  const settings = plan();
  assert.deepEqual(DeploySettingsRules.prompted(settings).map((item) => item.key), ["Events:Name"],
    "a linked key whose source holds no value waits on the source, not the owner");
  assert.deepEqual(DeploySettingsRules.linked(settings).map((item) => [item.key, item.target, item.from, item.value, item.services]), [
    ["Auth:Issuer", null, "Sites:identity:Url", null, ["events"]],
    ["Scfd:BaseUrl", "ocharo-dev", "Scfd:PublicUrl", null, ["events"]],
    ["Scfd:ClientSecret", "ocharo-dev", sourceKey, null, ["events"]],
    ["Scfd:TenantId", "ocharo-dev", "Scfd:Tenants:Events", "tenant-7", []],
  ]);
});

test("link puts the source with the settings action, and unlink deletes it; no value travels", async () => {
  const requests = [];
  globalThis.fetch = async (url, options) => {
    requests.push([url, options.method, options.headers.get("X-Wheelhouse-Action"), options.body ? JSON.parse(options.body) : null]);
    return options.method === "DELETE" ? new Response(null, { status: 204 }) : json({ data: { target: "ocharo-dev", key: sourceKey } });
  };
  const linked = await deploySettingsApi.link("events-dev", "Scfd:ClientSecret", { target: "ocharo-dev", key: sourceKey });
  assert.deepEqual([linked.ok, linked.value], [true, { target: "ocharo-dev", key: sourceKey }]);
  const unlinked = await deploySettingsApi.unlink("events-dev", "Scfd:ClientSecret");
  assert.equal(unlinked.ok, true);
  assert.deepEqual(requests, [
    ["/api/deployments/targets/events-dev/settings/Scfd%3AClientSecret/link", "PUT", "settings", { sourceTarget: "ocharo-dev", sourceKey }],
    ["/api/deployments/targets/events-dev/settings/Scfd%3AClientSecret/link", "DELETE", "settings", null],
  ]);
});

test("a refused link reads as the server's reason", async () => {
  globalThis.fetch = async () => json({ title: "Conflict", status: 409,
    detail: "ocharo-dev Desk:EventsKey is itself linked to events-dev Events:ApiKey; link to that key instead, since links go one hop." }, 409);
  const result = await deploySettingsApi.link("events-dev", "Scfd:ClientSecret", { target: "ocharo-dev", key: "Desk:EventsKey" });
  assert.deepEqual([result.ok, result.failure.status], [false, 409]);
});
