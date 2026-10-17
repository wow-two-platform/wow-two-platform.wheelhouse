import assert from "node:assert/strict";
import { after, test } from "node:test";
import { stop } from "esbuild";
import { bundleModule } from "./BundleModule.mjs";
import { container, product, target } from "./LoadModule.mjs";

const { secretBindingsOf, secretEnvironmentOf, hostedProductsOf, AttentionRules } = await bundleModule(`
  export { secretBindingsOf, secretEnvironmentOf } from './src/application/product/ProductSecrets';
  export { hostedProductsOf } from './src/application/servers/ServerHosting';
  export { AttentionRules } from './src/domain/overview/AttentionRules';
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

test("a server hosts the products its targets place on it, each with its environments there", () => {
  const targets = [
    target({ id: "pin-prod", product: "foreverpin", environment: "prod", serverId: "hel1" }),
    target({ id: "pin-dev", product: "foreverpin", environment: "dev", serverId: "hel1" }),
    target({ id: "nasiya-prod", product: "nasiya", environment: "prod", serverId: "hel1" }),
    target({ id: "vault-prod", product: "vault", environment: "prod", serverId: "ovh1" }),
  ];
  const catalog = [product(), product({ slug: "vault", name: "Secrets Vault", status: "inactive" })];
  assert.deepEqual(hostedProductsOf("hel1", targets, catalog),
    [{ slug: "foreverpin", name: "ForeverPin", environments: ["dev", "prod"], active: true },
      { slug: "nasiya", name: "nasiya", environments: ["prod"], active: true }]);
  assert.deepEqual(hostedProductsOf("ovh1", targets, catalog).map((item) => item.active), [false]);
  assert.deepEqual(hostedProductsOf("none", targets, catalog), []);
});

const host = (overrides = {}) => ({ cpus: 2, load: [0.4, 0.4, 0.4], memoryTotalBytes: 100, memoryAvailableBytes: 60,
  uptimeSeconds: 1, disks: [{ path: "/", totalBytes: 100, freeBytes: 50 }], ...overrides });
const vitalsOf = (servers, targets = []) => ({ collectedAt: "2026-10-17T00:00:00Z", servers, targets });

test("a server is degraded when it cannot be read, is overloaded, short on memory or nearly out of disk", () => {
  const unreachable = vitalsOf([{ serverId: "ovh1", ok: false, reason: "SSH timed out" }],
    [{ targetId: "nasiya-prod", serverId: "ovh1", ok: false, reason: "SSH timed out" }]);
  assert.deepEqual(AttentionRules.ofServer(unreachable, "ovh1").map((item) => [item.tone, item.detail]),
    [["danger", "SSH timed out"]]);

  const busy = vitalsOf([{ serverId: "hel1", ok: true, host: host({ load: [3.4, 3, 3], memoryAvailableBytes: 5,
    disks: [{ path: "/", totalBytes: 100, freeBytes: 20 }, { path: "/data", totalBytes: 100, freeBytes: 4 }] }) }]);
  assert.deepEqual(AttentionRules.ofServer(busy, "hel1").map((item) => item.id),
    ["disk:hel1:/data", "memory:hel1", "load:hel1"]);

  const healthy = vitalsOf([{ serverId: "hel1", ok: true, host: host() }],
    [{ targetId: "pin-prod", serverId: "hel1", ok: true, containers: [container({ restarts: 9 })] }]);
  assert.deepEqual(AttentionRules.ofServer(healthy, "hel1"), []);
  assert.deepEqual(AttentionRules.ofServer(healthy, "elsewhere"), []);
});
