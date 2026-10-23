import assert from "node:assert/strict";
import { after, test } from "node:test";
import { stop } from "esbuild";
import { bundleModule } from "./BundleModule.mjs";

// The built-in vault's editors: the type checks a field runs before it saves, how a plan becomes the Settings and
// Secrets lists, a failed deploy's hint, and the wire decoding of plans, saves and history.
const loaded = await bundleModule(`
  export { SettingTypes, EnvironmentSettingsRules } from './src/domain/settings';
  export { DeploySettingsRules } from './src/domain/deployments';
  export {
    DeploySettingsSchema, DeploySettingsSavedSchema, SecretHistorySchema, SettingsApplySchema,
  } from './src/integration/deployments/settingsSchemas';
  export { DeploymentItemSchema } from './src/integration/deployments/historySchemas';
`);
after(() => stop());
const {
  SettingTypes, EnvironmentSettingsRules, DeploySettingsRules, DeploySettingsSchema, DeploySettingsSavedSchema,
  SecretHistorySchema, SettingsApplySchema, DeploymentItemSchema,
} = loaded;

const key = (name, source, extra = {}) => ({
  key: name, required: false, secret: false, source, value: null, default: null, derived: null, allowed: null, ...extra,
});

const plan = () => DeploySettingsSchema.parse({
  target: "foreverpin-prod", product: "foreverpin", environment: "prod", release: "v1.2.0", bundle: "foreverpin-gh-9",
  firstDeploy: false, missing: 1,
  vault: { id: "built-in", namespace: "foreverpin-prod", reachable: true, mode: "builtin", sealed: null },
  services: [
    {
      name: "management", hostFile: false,
      keys: [
        key("AllowedHosts", "derived", { required: true, derived: "hosts", value: "pin.example;localhost" }),
        key("Billing:SecretKey", "vault", {
          required: true, secret: true,
          vault: { key: "Billing:SecretKey", version: 3, updatedAt: "2026-10-19T10:00:00Z", updatedBy: "max",
            fingerprint: "0123456789abcdef", expiresAt: "2026-10-25T00:00:00Z", expiry: "7d" },
        }),
        key("Billing:Mode", "stored", { type: "choice", allowed: ["test", "live"], value: "live" }),
        key("Jobs:Cleanup", "default", { type: "cron", default: "0 3 * * *" }),
        key("Support:Email", "missing", { required: true, type: "email" }),
      ],
    },
    {
      name: "redirect", hostFile: false,
      keys: [key("Billing:Mode", "stored", { type: "choice", allowed: ["test", "live"], value: "live" })],
    },
  ],
  extra: [key("Promo:Banner", "stored", { adHoc: true, value: "Autumn" }), key("Mail:ApiKey", "vault", { secret: true })],
  unapplied: ["Billing:Mode"],
  pending: { at: "2026-10-20T03:00:00Z", by: "max" },
});

test("each type accepts what Wheelhouse accepts on save, and the check never quotes the value", () => {
  const fits = [["number", "42"], ["number", "-0.5"], ["boolean", "false"], ["url", "https://pin.example/a?b=1"],
    ["cron", "*/15 9-17 * * MON-FRI"], ["cron", "0 0 3 L * ?"], ["cron", "@daily"], ["date", "2027-02-28"], ["email", "owner@example.com"]];
  for (const [type, value] of fits) assert.equal(SettingTypes.check(type, value), null, `${type} ${value}`);
  const misfits = [["number", "1e3"], ["number", "007"], ["boolean", "True"], ["url", "pin.example"], ["cron", "60 * * * *"],
    ["cron", "0 3 * *"], ["date", "2027-02-30"], ["email", "owner"]];
  for (const [type, value] of misfits) assert.notEqual(SettingTypes.check(type, value), null, `${type} ${value}`);
  assert.equal(SettingTypes.check("choice", "live", null, ["test", "live"]), null);
  assert.equal(SettingTypes.check("choice", "sandbox", null, ["test", "live"]), "must be one of test, live");
  assert.equal(SettingTypes.check("date", "31.01.2027 18:30", "dd.MM.yyyy HH:mm"), null);
  assert.equal(SettingTypes.check("date", "2027-01-31", "dd.MM.yyyy"), "must be a date as dd.MM.yyyy");
  assert.ok(!SettingTypes.check("number", "hunter2").includes("hunter2"));
});

test("key names read as secrets the way the server and runner guess them", () => {
  for (const name of ["Billing:SecretKey", "Smtp:Password", "Github:Token", "ConnectionStrings:Reports", "Db:ConnectionString"])
    assert.ok(SettingTypes.looksSecret(name), name);
  for (const name of ["Auth:Google:ClientId", "DataProtection:KeysPath", "Billing:Prices:Solo"])
    assert.ok(!SettingTypes.looksSecret(name), name);
  assert.ok(SettingTypes.isDateFormat("dd.MM.yyyy HH:mm") && !SettingTypes.isDateFormat("%Y-%m"));
});

test("a plan lists each key once, settings and secrets apart, derived ones aside", () => {
  const settings = EnvironmentSettingsRules.settings(plan());
  assert.deepEqual(settings.map((row) => [row.key, row.source, row.services]), [
    ["Support:Email", "missing", ["management"]],
    ["Billing:Mode", "stored", ["management", "redirect"]],
    ["Promo:Banner", "stored", []],
    ["Jobs:Cleanup", "default", ["management"]],
  ]);
  assert.equal(settings.find((row) => row.key === "Billing:Mode").unapplied, true);
  const secrets = EnvironmentSettingsRules.secrets(plan());
  assert.deepEqual(secrets.map((row) => [row.key, row.vault?.version ?? null]), [["Billing:SecretKey", 3], ["Mail:ApiKey", null]]);
  assert.deepEqual(EnvironmentSettingsRules.derived(plan()).map((row) => row.key), ["AllowedHosts"]);
  assert.equal(EnvironmentSettingsRules.expiryLabel(secrets[0].vault.expiry), "expires within 7 days");
  assert.equal(EnvironmentSettingsRules.expiryTone("expired"), "danger");
});

test("the deploy dialog types the fields it asks for", () => {
  const prompted = DeploySettingsRules.prompted(plan());
  const email = prompted.find((item) => item.key === "Support:Email");
  assert.deepEqual([email.type, email.required, email.declaredSecret], ["email", true, false]);
});

test("a failed deploy names the settings it changed as the likely cause", () => {
  const changes = [
    { key: "Billing:SecretKey", kind: "secret", change: "changed", from: 3, to: 4 },
    { key: "Billing:Region", kind: "setting", change: "added", from: null, to: null },
  ];
  assert.equal(EnvironmentSettingsRules.hint(changes, true),
    "Changed settings: Billing:SecretKey v3→v4, Billing:Region (new) — likely cause");
  assert.equal(EnvironmentSettingsRules.hint(changes, false), "Changed settings: Billing:SecretKey v3→v4, Billing:Region (new)");
  assert.equal(EnvironmentSettingsRules.hint([], true), null);
  const item = DeploymentItemSchema.parse({
    id: "j1", status: "rolled_back", outcome: "failed", submittedAt: "2026-10-19T10:00:00Z", settingsChanged: changes,
    settingsSuspect: true,
  });
  assert.deepEqual([item.settingsChanged.length, item.settingsSuspect], [2, true]);
  const older = DeploymentItemSchema.parse({ id: "j0", status: "succeeded", outcome: "succeeded", submittedAt: "2026-10-19T10:00:00Z" });
  assert.deepEqual([older.settingsChanged, older.settingsSuspect], [null, false]);
});

test("an older plan without the vault's fields still reads, and saves and history decode without values", () => {
  const legacy = DeploySettingsSchema.parse({
    target: "t", product: "p", environment: "dev", release: "v1", firstDeploy: true, missing: 0,
    vault: { id: "control-vault", namespace: "p-dev", reachable: true },
    services: [{ name: "api", hostFile: false, keys: [key("A:B", "missing", { required: true })] }],
  });
  assert.deepEqual([legacy.vault.mode, legacy.extra, legacy.unapplied, legacy.pending, legacy.services[0].keys[0].type],
    ["builtin", [], [], null, "text"]);
  const saved = DeploySettingsSavedSchema.parse({ settings: 0, secrets: 1,
    keys: [{ key: "A:Key", secret: true, removed: false, version: 2, fingerprint: "ab" }] });
  assert.equal(saved.keys[0].version, 2);
  const history = SecretHistorySchema.parse({ target: "t", key: "A:Key", removed: false, expiresAt: null,
    versions: [{ version: 2, createdBy: "max", createdAt: "2026-10-19T10:00:00Z", fingerprint: "ab", note: null,
      origin: "restored from v1", current: true }] });
  assert.equal(history.versions[0].origin, "restored from v1");
  assert.equal(SettingsApplySchema.parse({ target: "t", mode: "scheduled", at: "2026-10-20T03:00:00Z", by: "max" }).deployment, null);
});

test("a generated secret travels as a format with no value, for text keys only, and its save decodes without one", () => {
  assert.deepEqual(SettingTypes.generated("Auth:SigningKey"),
    { key: "Auth:SigningKey", value: "", secret: true, generate: "hex" });
  assert.equal(SettingTypes.generated("Auth:SigningKey", "base64url").generate, "base64url");
  assert.deepEqual([...SettingTypes.secretFormats], ["hex", "base64url"]);
  assert.equal(SettingTypes.canGenerate("text"), true);
  assert.equal(SettingTypes.canGenerate(undefined), true, "an undeclared key is text");
  for (const type of ["number", "boolean", "choice", "url", "cron", "date", "email"]) {
    assert.equal(SettingTypes.canGenerate(type), false, type);
  }
  const saved = DeploySettingsSavedSchema.parse({ settings: 0, secrets: 1,
    keys: [{ key: "Auth:SigningKey", secret: true, removed: false, version: 3, fingerprint: "ab", generated: true }] });
  assert.deepEqual([saved.keys[0].version, saved.keys[0].generated], [3, true]);
  assert.equal(Object.values(saved.keys[0]).some((value) => typeof value === "string" && value.length >= 43), false);
});

test("an apply is scheduled from five minutes on and within 90 days", () => {
  const { min, max } = EnvironmentSettingsRules.scheduleBounds(new Date("2026-10-19T12:00:00Z"));
  assert.ok(min < max);
  assert.match(min, /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/);
});
