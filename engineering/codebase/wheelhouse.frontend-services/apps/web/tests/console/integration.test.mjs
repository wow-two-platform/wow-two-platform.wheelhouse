import assert from "node:assert/strict";
import { after, afterEach, test } from "node:test";
import { resolve } from "node:path";
import { build, stop } from "esbuild";

// Bundle the real integration modules in memory; no app server, browser, or live API is needed.
const bundle = await build({
  stdin: {
    contents: `
      export { productsApi } from './src/integration/products';
      export { integrationKeysApi } from './src/integration/integrations';
      export { deploymentsApi } from './src/integration/deployments';
      export { authApi } from './src/integration/auth';
      export { secretsApi } from './src/integration/secrets';
      export { serversApi } from './src/integration/servers';
      export { targetsApi } from './src/integration/targets';
      export { auditApi } from './src/integration/audit';
      export { AuditArea, AuditExtensions } from './src/domain/audit';
      export { compareEnvironments } from './src/domain/deployments/EnvironmentComparison';
      export { hostTrend } from './src/domain/servers/models/VitalsSample';
      export { ServerExtensions } from './src/domain/servers/ServerExtensions';
      export { AttentionRules } from './src/domain/overview/AttentionRules';
      export { sessionBridge } from './src/integration/common';
      export { requestLiveVitals, useServerVitals } from './src/application/servers/useServerVitals';
      export { queryClient, queryPlugin } from './src/bootstrap/query';
      export { createRenderer, h, nextTick, reactive } from 'vue';
    `,
    resolveDir: process.cwd(),
  },
  alias: { "@": resolve("src") },
  bundle: true,
  platform: "node",
  format: "esm",
  write: false,
});
const {
  productsApi,
  integrationKeysApi,
  deploymentsApi,
  authApi,
  secretsApi,
  serversApi,
  targetsApi,
  auditApi,
  AuditArea,
  AuditExtensions,
  compareEnvironments,
  hostTrend,
  ServerExtensions,
  AttentionRules,
  sessionBridge,
  useServerVitals,
  requestLiveVitals,
  queryClient,
  queryPlugin,
  createRenderer,
  h,
  nextTick,
  reactive,
} = await import(
  `data:text/javascript;base64,${Buffer.from(bundle.outputFiles[0].text).toString("base64")}`
);
const originalFetch = globalThis.fetch;
afterEach(() => {
  globalThis.fetch = originalFetch;
});
after(() => stop());

function json(data, status = 200, headers = {}) {
  return new Response(JSON.stringify(data), {
    status,
    headers: { "Content-Type": "application/json", ...headers },
  });
}

const product = {
  slug: "test",
  name: "Test",
  description: "A test product.",
  id: "p-test",
  workspaceId: "w-ventures",
  status: "active",
  inactiveServices: [],
  repository: { name: "owner/repository", url: "https://github.com/owner/repository", defaultBranch: "main" },
  release: { asset: "test-release.tar.gz", workflow: null, images: [{ service: "api", image: "ghcr.io/owner/test/api" }] },
  homepage: null,
  identity: { source: "record", commit: null, problem: null, services: [] },
  iconUrl: "/api/products/test/icon",
  environments: [
    { name: "dev", sites: [{ name: "app", url: "https://dev.example.com", exposure: "public" }],
      secrets: { vault: "pilot-vault", namespace: "test-dev" } },
    { name: "prod", sites: [], secrets: null },
  ],
};

test("serializes a status write once with its action and same-origin cookie credentials", async () => {
  const requests = [];
  globalThis.fetch = async (url, options) => {
    requests.push({ url, options });
    return requests.length === 1 ? json({ data: { ...product, status: "inactive" } }) : new Response(null, { status: 204 });
  };
  assert.equal((await productsApi.updateStatus("p-test", "inactive")).ok, true);
  assert.equal((await productsApi.updateServiceStatus("p-test", "api worker", "active")).ok, true);
  assert.equal((await productsApi.moveToWorkspace("p-test", "w-platform")).ok, true);
  assert.deepEqual(
    requests.map(({ url, options }) => [url, options.method, options.headers.get("X-Wheelhouse-Action"), JSON.parse(options.body)]),
    [
      ["/api/products/p-test/status", "PUT", "status", { status: "inactive" }],
      ["/api/products/p-test/services/api%20worker/status", "PUT", "status", { status: "active" }],
      ["/api/products/p-test/workspace", "PUT", "workspace", { workspaceId: "w-platform" }],
    ],
  );
  assert.equal(requests[0].options.credentials, "same-origin");
});

test("writes inventory rows with their own actions and decodes what the server stored", async () => {
  const server = { slug: "hel1", name: "Helsinki", provider: "hetzner", host: "vps.example.net", region: "hel1",
    sshUser: "deploy", sshPort: 22, ingress: { scheme: "https", port: null, entryPoints: ["websecure"],
      privateEntryPoints: [], certResolver: "letsencrypt", pattern: null, probe: null, privateProbe: null } };
  const target = { slug: "test-dev", product: "test", server: "hel1", environment: "dev", network: "platform",
    root: "/srv/wheelhouse", settings: [{ service: "api", path: "/srv/settings/test/api.json" }],
    smokeChecks: [{ service: "api", path: "/health", status: 200 }], sites: [{ site: "app", host: "dev.example.com" }] };
  const requests = [];
  globalThis.fetch = async (url, options) => {
    requests.push({ url, options });
    if (options.method === "DELETE") return new Response(null, { status: 204 });
    return json({ data: url.startsWith("/api/servers") ? server : url.startsWith("/api/targets") ? target : product });
  };
  const { slug: _, ...serverBody } = server;
  assert.deepEqual((await serversApi.createServer(server)).value, server);
  assert.deepEqual((await serversApi.updateServer("hel1", serverBody)).value, server);
  assert.deepEqual((await targetsApi.updateTarget("test-dev", target)).value, target);
  assert.equal((await secretsApi.deleteVault("hel1-vault")).ok, true);
  assert.equal((await productsApi.deleteProduct("test")).ok, true);
  assert.deepEqual(
    requests.map(({ url, options }) => [url, options.method, options.headers.get("X-Wheelhouse-Action")]),
    [
      ["/api/servers", "POST", "server"],
      ["/api/servers/hel1", "PUT", "server"],
      ["/api/targets/test-dev", "PUT", "target"],
      ["/api/vaults/hel1-vault", "DELETE", "vault"],
      ["/api/products/test", "DELETE", "product"],
    ],
  );
  assert.equal("slug" in JSON.parse(requests[1].options.body), false);

  globalThis.fetch = async () => json({ data: [{ ...target, environment: "staging" }] });
  const refused = await targetsApi.listTargets("test");
  assert.equal(refused.ok, false);
  assert.equal(refused.failure.code, "protocol");
});

test("round trips supported server providers and rejects an unknown provider", async () => {
  for (const provider of ["hetzner", "local", "ovhcloud", "contabo"]) {
    const server = { slug: "pilot", name: "Pilot", provider, host: "vps.example.net", region: "pilot",
      sshUser: "deploy", sshPort: 22, ingress: { scheme: "https", port: null, entryPoints: ["websecure"],
        privateEntryPoints: [], certResolver: "letsencrypt", pattern: null, probe: null, privateProbe: null } };
    const bodies = [];
    globalThis.fetch = async (_url, options) => {
      if (options.body) bodies.push(JSON.parse(options.body));
      return json({ data: options.method === "GET" ? [server] : server });
    };
    const { slug, ...update } = server;
    assert.deepEqual((await serversApi.createServer(server)).value, server);
    assert.deepEqual((await serversApi.updateServer(slug, update)).value, server);
    assert.deepEqual((await serversApi.listServers()).value, [server]);
    assert.deepEqual(bodies.map((body) => body.provider), [provider, provider]);

    globalThis.fetch = async () => json({ data: [{ ...server, provider: "unsupported" }] });
    const refused = await serversApi.listServers();
    assert.equal(refused.ok, false);
    assert.equal(refused.failure.code, "protocol");
  }
  // The host reads the server at a private address as the control host; a host before that rule says nothing.
  globalThis.fetch = async () => json({ data: [{ slug: "control", name: "Control", provider: "ovhcloud", host: "172.30.0.1",
    region: "control", sshUser: "wheelhouse-deploy", sshPort: 22, controlHost: true, ingress: { scheme: "https", port: null,
      entryPoints: ["private"], privateEntryPoints: [], certResolver: null, pattern: null, probe: null, privateProbe: null } }] });
  assert.equal((await serversApi.listServers()).value[0].controlHost, true);
  // A server's own console address reads back; a host that omits it leaves the provider's default.
  globalThis.fetch = async () => json({ data: [{ slug: "bhs1", name: "Beauharnois", provider: "ovhcloud", host: "bhs1.example.net",
    region: "bhs", sshUser: "deploy", sshPort: 22, consoleUrl: "https://ca.ovh.com/manager/", ingress: { scheme: "https", port: null,
      entryPoints: ["websecure"], privateEntryPoints: [], certResolver: null, pattern: null, probe: null, privateProbe: null } }] });
  assert.equal((await serversApi.listServers()).value[0].consoleUrl, "https://ca.ovh.com/manager/");
});

test("creates and revokes integration keys with explicit actions and reads the secret once", async () => {
  const key = { id: "key-id", name: "Claude", prefix: "wh_abcdefgh", scopes: ["catalog:read"], createdBy: "max",
    createdAt: "2026-09-30T00:00:00Z", lastUsedAt: null, revokedAt: null };
  const requests = [];
  globalThis.fetch = async (url, options) => {
    requests.push({ url, options });
    return json({ data: url.endsWith("/revoke")
      ? { ...key, revokedAt: "2026-09-30T01:00:00Z" }
      : { key, secret: "wh_" + "a".repeat(32) } });
  };
  const created = await integrationKeysApi.createKey({ name: "Claude", scopes: ["catalog:read"] });
  assert.equal(created.value.secret, "wh_" + "a".repeat(32));
  assert.equal((await integrationKeysApi.revokeKey("key-id")).value.revokedAt, "2026-09-30T01:00:00Z");
  assert.deepEqual(requests.map(({ url, options }) => [url, options.method, options.headers.get("X-Wheelhouse-Action")]),
    [["/api/integration-keys", "POST", "key-create"], ["/api/integration-keys/key-id/revoke", "POST", "key-revoke"]]);
});

test("preserves explicit deployment, reconciliation, and vault action headers", async () => {
  const requests = [];
  globalThis.fetch = async (url, options) => {
    requests.push({ url, options });
    return json({
      data: url.startsWith("/api/vaults")
        ? null
        : { id: "job", status: "queued" },
    });
  };
  assert.equal(
    (await deploymentsApi.startDeployment("target", "release")).ok,
    true,
  );
  assert.equal((await deploymentsApi.reconcile("target", "job")).ok, true);
  assert.equal(
    (await secretsApi.createNamespace("vault", "space", "Space")).ok,
    true,
  );
  assert.deepEqual(
    requests.map(({ options }) => options.headers.get("X-Wheelhouse-Action")),
    ["deploy", "reconcile", "vault"],
  );
  assert.deepEqual(JSON.parse(requests[0].options.body), {
    target: "target",
    release: "release",
  });
  assert.deepEqual(JSON.parse(requests[1].options.body), { job: "job" });
});

test("sends a typed prod confirmation and an explicit build action", async () => {
  const requests = [];
  globalThis.fetch = async (url, options) => {
    requests.push({ url, options });
    return json({
      data: url.includes("/builds")
        ? { product: "foreverpin", commit: "e".repeat(40), status: "requested" }
        : { id: "job", status: "queued" },
    });
  };
  assert.equal(
    (
      await deploymentsApi.startDeployment(
        "foreverpin-prod",
        "release",
        "foreverpin-prod",
      )
    ).ok,
    true,
  );
  assert.equal(
    (
      await deploymentsApi.startDeployment(
        "foreverpin-prod",
        "release",
        "foreverpin-prod",
        true,
      )
    ).ok,
    true,
  );
  const build = await deploymentsApi.requestBuild("foreverpin", "e".repeat(40));
  assert.equal(build.ok, true);
  assert.deepEqual(JSON.parse(requests[0].options.body), {
    target: "foreverpin-prod",
    release: "release",
    confirm: "foreverpin-prod",
  });
  assert.deepEqual(JSON.parse(requests[1].options.body), {
    target: "foreverpin-prod",
    release: "release",
    confirm: "foreverpin-prod",
    skipDevPass: true,
  });
  assert.equal(
    requests[2].url,
    "/api/deployments/products/foreverpin/builds",
  );
  assert.equal(requests[2].options.headers.get("X-Wheelhouse-Action"), "build");
  assert.deepEqual(JSON.parse(requests[2].options.body), {
    commit: "e".repeat(40),
  });
});

test("decodes environment rules, commit builds, and only http site links", async () => {
  globalThis.fetch = async (url) => {
    if (url.endsWith("/targets"))
      return json({
        data: [
          { id: "foreverpin-dev", product: "foreverpin", environment: "dev",
            serverId: "local", provider: "Local", host: "127.0.0.1",
            acceptsCandidates: true, needsConfirmation: false },
          { id: "legacy", product: "foreverpin", environment: "prod",
            serverId: "local", provider: "Local", host: "127.0.0.1" },
        ],
      });
    if (url.endsWith("/releases"))
      return json({
        data: [
          { id: "foreverpin-ci-7", product: "foreverpin", release: "sha-ccccccc",
            kind: "candidate", commit: "c".repeat(40), branch: "main",
            prerelease: true, publishedAt: "2026-09-27T10:00:00Z", provider: "GitHubActions" },
          { id: "foreverpin-gh-1", product: "foreverpin", release: "v0.9.0",
            prerelease: false, publishedAt: "2026-09-19T00:00:00Z", provider: "GitHubReleases" },
        ],
      });
    if (url.includes("/commits?branch=feature%2Fpins"))
      return json({ data: [{ sha: "c".repeat(40), message: "feat: pins", author: "Max", buildId: null }] });
    const site = url.includes("unsafe") ? "javascript:alert(1)" : "http://app-foreverpin.dev.localhost:18080";
    return json({
      data: { targetId: "foreverpin-dev", project: "foreverpin-dev", condition: "ready", active: null,
        current: { id: "job", release: "sha-ccccccc", kind: "candidate",
          versions: { management: { version: "1.2.0+ccccccc", changedIn: "sha-ccccccc" } },
          sites: [{ name: "app", service: "management", exposure: "public", url: site }] } },
    });
  };
  const targets = await deploymentsApi.listTargets();
  assert.deepEqual(
    targets.value.map((item) => [item.acceptsCandidates, item.needsConfirmation, item.requiresDevPass]),
    [[true, false, false], [false, false, false]],
  );
  const releases = await deploymentsApi.listReleases();
  assert.deepEqual(releases.value.map((item) => item.kind), ["candidate", "release"]);
  const commits = await deploymentsApi.listCommits("foreverpin", "feature/pins");
  assert.equal(commits.value[0].buildId, null);
  const state = await deploymentsApi.getTargetState("foreverpin-dev");
  assert.equal(state.value.current.sites[0].url, "http://app-foreverpin.dev.localhost:18080");
  assert.equal(state.value.current.versions.management.version, "1.2.0+ccccccc");
  const unsafe = await deploymentsApi.getTargetState("unsafe");
  assert.equal(unsafe.ok, false);
  assert.equal(unsafe.failure.code, "protocol");
});

test("keeps rollout steps, warnings and site probes, and refuses an unknown step status", async () => {
  const outcome = {
    id: "job",
    status: "succeeded",
    steps: [
      { name: "Pull images", status: "succeeded", startedAt: "2026-09-28T10:00:00Z", completedAt: "2026-09-28T10:00:04Z", detail: "2 images" },
      { name: "Probe sites", status: "warning", detail: "1 of 2 sites answered" },
    ],
    warnings: ["Site go did not answer through the ingress: The ingress has no route for this host"],
    sites: [{ name: "go", service: "redirect", exposure: "public", url: "http://go.localhost:18080",
      probe: { ok: false, status: 404, detail: "The ingress has no route for this host" } }],
  };
  globalThis.fetch = async () => json({ data: outcome });
  const result = await deploymentsApi.getOutcome("job");
  assert.equal(result.ok, true);
  assert.deepEqual(result.value.steps.map((step) => step.status), ["succeeded", "warning"]);
  assert.equal(result.value.warnings.length, 1);
  assert.equal(result.value.sites[0].probe.ok, false);
  globalThis.fetch = async () => json({ data: { ...outcome, steps: [{ name: "Pull images", status: "paused" }] } });
  assert.equal((await deploymentsApi.getOutcome("job")).ok, false);
});

test("reads one service's logs with a bounded tail and decodes the lines", async () => {
  let requested;
  globalThis.fetch = async (url) => {
    requested = url;
    return json({ data: { targetId: "foreverpin-dev", project: "foreverpin-dev", service: "redirect", tail: 50,
      collectedAt: "2026-09-28T10:00:00Z", lines: ["2026-09-28T10:00:00Z started"], truncated: false } });
  };
  const result = await deploymentsApi.getLogs("foreverpin-dev", "redirect", 50);
  assert.equal(result.ok, true);
  assert.equal(requested, "/api/deployments/targets/foreverpin-dev/services/redirect/logs?tail=50");
  assert.deepEqual(result.value.lines, ["2026-09-28T10:00:00Z started"]);
});

test("pages the audit trail by token and names each action and chain break in words", async () => {
  const requests = [];
  const entry = { sequence: 3, occurredAt: "2026-09-28T10:00:00Z", actor: "max", action: "deployment.start",
    subject: "foreverpin-dev", outcome: "succeeded", detail: "v1", reason: null };
  globalThis.fetch = async (url) => {
    requests.push(url);
    return json({ data: url.includes("verification")
      ? { intact: false, entries: 3, brokenSequence: 2, reason: "HashMismatch" }
      : url.includes("pageToken") ? { items: [{ ...entry, sequence: 1 }], pageSize: 100 }
        : { items: [entry], pageSize: 100, nextPageToken: "WzJd" } });
  };
  const newest = (await auditApi.list(100)).value;
  assert.deepEqual([newest.items[0].sequence, newest.nextPageToken], [3, "WzJd"]);
  // The last page names no token.
  const oldest = (await auditApi.list(100, newest.nextPageToken)).value;
  assert.deepEqual([oldest.items[0].sequence, oldest.nextPageToken], [1, null]);
  assert.equal((await auditApi.verify()).value.brokenSequence, 2);
  await auditApi.list(50, null, "transcript forge");
  assert.deepEqual(requests,
    ["/api/audit?pageSize=100", "/api/audit?pageSize=100&pageToken=WzJd", "/api/audit/verification",
      "/api/audit?pageSize=50&product=transcript%20forge"]);
  assert.equal(AuditExtensions.label("service.status"), "Changed a service’s status");
  assert.equal(AuditExtensions.label("vault.secret.set"), "Set a secret");
  assert.equal(AuditExtensions.label("future.action"), "future.action");
  assert.equal(AuditExtensions.inArea("build.request", AuditArea.Deployments), true);
  assert.equal(AuditExtensions.inArea("vault.token.mint", AuditArea.Products), false);
  assert.equal(AuditExtensions.breakLabel("HashMismatch"), "an entry was edited");
});

test("lines environments up dev to prod and offers each published release to the next one", () => {
  const target = (environment) => ({ id: `pin-${environment}`, product: "pin", environment, serverId: "local",
    provider: "Local", host: "127.0.0.1", acceptsCandidates: environment === "dev", needsConfirmation: false,
    requiresDevPass: environment === "prod" });
  const state = (release, kind, versions) => ({ targetId: "", project: "", condition: "ready", active: null,
    current: { id: "job", release, kind, versions } });
  const releases = [{ id: "pin-v1-1", product: "pin", release: "v1.1", kind: "release", prerelease: false,
    publishedAt: "2026-09-28T00:00:00Z", provider: "github" }];
  const comparison = compareEnvironments(
    [target("prod"), target("dev")],
    [
      state("v1.0", "release", { api: { version: "1.0", changedIn: "v1.0" } }),
      state("v1.1", "release", { api: { version: "1.1", changedIn: "v1.1" } }),
    ],
    releases,
  );
  assert.deepEqual(comparison.columns.map((column) => column.environment), ["dev", "prod"]);
  assert.deepEqual(comparison.columns[0].promotion,
    { targetId: "pin-prod", environment: "prod", bundleId: "pin-v1-1", release: "v1.1" });
  assert.equal(comparison.columns[1].promotion, null); // prod is the end of the path
  assert.deepEqual(comparison.rows[0].pending, [true, false]);
  const built = compareEnvironments([target("dev"), target("prod")],
    [state("sha-abc", "candidate", { api: { version: "1.1+abc", changedIn: "sha-abc" } }),
      state("v1.0", "release", { api: { version: "1.0", changedIn: "v1.0" } })], releases);
  assert.equal(built.columns[0].promotion, null); // a commit's build never leaves dev
  const unread = compareEnvironments([target("dev"), target("prod")],
    [state("v1.1", "release", {}), undefined], releases);
  assert.equal(unread.columns[0].promotion, null);
  assert.equal(unread.columns[1].read, false);
});

test("reads vitals history and traces each server's host figures from its own readings", async () => {
  const sample = (targetId, serverId, sampledAt, memoryPercent) => ({ targetId, serverId, sampledAt, readable: true,
    loadPercent: null, memoryPercent, diskPercent: targetId ? null : 40, containers: targetId ? 2 : 0,
    healthyContainers: targetId ? 2 : 0, restarts: 0 });
  let requested;
  globalThis.fetch = async (url) => {
    requested = url;
    return json({ data: [
      sample(null, "local", "2026-09-28T10:00:00Z", 50),
      sample("pin-dev", "local", "2026-09-28T10:00:00Z", null),
      sample(null, "local", "2026-09-28T10:05:00Z", null),
      sample(null, "local", "2026-09-28T10:10:00Z", 70),
      // A server that runs no target still has a trend.
      sample(null, "fresh", "2026-09-28T10:10:00Z", 10),
    ] });
  };
  const result = await serversApi.getVitalsHistory(24);
  assert.equal(requested, "/api/deployments/vitals/history?hours=24");
  assert.deepEqual(hostTrend(result.value, "local", "memoryPercent").map((point) => point.value), [50, 70]);
  assert.deepEqual(hostTrend(result.value, "fresh", "diskPercent").map((point) => point.value), [40]);
  assert.deepEqual(hostTrend(result.value, "missing", "memoryPercent"), []);
});

test("a server's host vitals come from the server itself, so one with no target shows them", async () => {
  const host = (memoryAvailableBytes) => ({ cpus: 2, load: [1, 1, 1], memoryTotalBytes: 100, memoryAvailableBytes,
    uptimeSeconds: 60, disks: [{ path: "/srv", totalBytes: 100, freeBytes: 5 }] });
  globalThis.fetch = async () => json({ data: {
    collectedAt: "2026-10-17T00:00:00Z",
    servers: [
      { serverId: "fresh", ok: true, host: host(5), problems: [] },
      { serverId: "dark", ok: false, reason: "Missing SSH identity file" },
    ],
    targets: [],
  } });
  const vitals = (await serversApi.getVitals()).value;
  assert.equal(ServerExtensions.memoryPercent(ServerExtensions.hostOf(vitals, "fresh")), 95);
  assert.equal(ServerExtensions.hostOf(vitals, "dark"), null);
  assert.equal(ServerExtensions.readingOf(vitals, "dark").reason, "Missing SSH identity file");
  assert.equal(ServerExtensions.hostOf(vitals, "missing"), null);
  assert.deepEqual(AttentionRules.fromVitals(vitals).map((item) => item.id).sort(),
    ["disk:fresh:/srv", "memory:fresh"]);
});

test("accepts an explicit empty logout success", async () => {
  globalThis.fetch = async () => new Response(null, { status: 204 });
  assert.deepEqual(await authApi.signOut(), { ok: true, value: undefined });
});

test("reads what deploys each product, Wheelhouse when the host names nothing", async () => {
  globalThis.fetch = async () => json({ data: [product, { ...product, slug: "wheelhouse", deploy: "ci" }] });
  const result = await productsApi.listProducts();
  assert.deepEqual(result.value.map((item) => [item.slug, item.deploy]), [["test", "wheelhouse"], ["wheelhouse", "ci"]]);
});

test("rejects a missing management envelope and malformed consumed product fields", async () => {
  for (const payload of [
    [product],
    { data: [{ ...product, status: "invented" }] },
    { data: [{ ...product, deploy: "sometimes" }] },
    { data: [{ ...product, inactiveServices: undefined }] },
    { data: [{ slug: "partial" }] },
  ]) {
    globalThis.fetch = async () => json(payload);
    const result = await productsApi.listProducts();
    assert.equal(result.ok, false);
    assert.equal(result.failure.code, "protocol");
  }
});

test("preserves HTTP status, headers and field diagnostics without displaying server details", async () => {
  const problem = {
    detail: "internal server diagnostics",
    errors: { slug: ["Duplicate slug"] },
  };
  globalThis.fetch = async () => json(problem, 409, { "Retry-After": "10" });
  const result = await productsApi.listProducts();
  assert.equal(result.ok, false);
  assert.equal(result.failure.status, 409);
  assert.equal(result.failure.headers["retry-after"], "10");
  assert.deepEqual(result.failure.problem.errors, problem.errors);
  assert.notEqual(result.failure.message, problem.detail);
});

test("fills embedded readiness state target from the runner transport outer identifier", async () => {
  globalThis.fetch = async () =>
    json({
      data: {
        targetId: "target",
        ok: true,
        checks: [{ name: "State", ok: true, detail: "ready" }],
        state: {
          project: "project",
          condition: "ready",
          current: null,
          active: null,
        },
      },
    });
  const result = await deploymentsApi.checkTarget("target");
  assert.equal(result.ok, true);
  assert.equal(result.value.state.targetId, "target");
});

test("accepts unavailable targets and nullable host metrics without inventing healthy data", async () => {
  const snapshot = {
    collectedAt: "2026-09-26T00:00:00Z",
    servers: [
      {
        serverId: "server",
        ok: true,
        host: {
          cpus: null,
          load: null,
          memoryTotalBytes: null,
          memoryAvailableBytes: null,
          uptimeSeconds: null,
          disks: [],
        },
        problems: [],
      },
      { serverId: "dark", ok: false, reason: "SSH unavailable" },
    ],
    targets: [
      {
        targetId: "unreachable",
        serverId: "server",
        ok: false,
        reason: "SSH unavailable",
      },
      {
        targetId: "online",
        serverId: "server",
        ok: true,
        containers: null,
        problems: ["Container details unavailable"],
      },
    ],
  };
  globalThis.fetch = async () => json({ data: snapshot });
  const result = await serversApi.getVitals();
  assert.equal(result.ok, true);
  assert.deepEqual(result.value, snapshot);
});

test("rejects malformed minted tokens instead of creating a one-time reveal without a token", async () => {
  globalThis.fetch = async () =>
    json({ data: { id: "id", name: "token", namespace: "space" } });
  const result = await secretsApi.mintToken("vault", "space", "token");
  assert.equal(result.ok, false);
  assert.equal(result.failure.code, "protocol");
});

test("cancels pending private data across logout even when the transport resolves late", async () => {
  let settle;
  globalThis.fetch = () =>
    new Promise((resolve) => {
      settle = resolve;
    });
  sessionBridge.publishSession({ status: "authenticated", user: { login: "owner", name: "Owner", avatar: null } });
  const pending = productsApi.listProducts();
  await new Promise((resolve) => setTimeout(resolve, 0));
  sessionBridge.publishSession({ status: "anonymous", user: null });
  const result = await pending;
  assert.equal(result.ok, false);
  assert.equal(result.failure.code, "cancelled");
  settle(json({ data: [product] }));
});

test("polling reads the console's shared vitals snapshot, and only a requested live read reads the hosts now", async () => {
  const snapshot = { collectedAt: "2026-10-27T00:00:00Z", targets: [] };
  const requested = [];
  let app;
  const renderer = createRenderer({
    createElement: () => ({}),
    createText: () => ({}),
    createComment: () => ({}),
    insert() {},
    remove() {},
    patchProp() {},
    setText() {},
    setElementText() {},
    parentNode: () => null,
    nextSibling: () => null,
  });
  try {
    globalThis.fetch = async (url) => {
      requested.push(url);
      return json({ data: snapshot });
    };
    queryClient.setQueryData(["servers", "vitals"], snapshot);
    let vitals;
    app = renderer.createApp({
      setup() {
        vitals = useServerVitals();
        return () => null;
      },
    });
    app.use(queryPlugin(queryClient));
    app.mount({});
    await vitals.refetch();
    requestLiveVitals();
    await vitals.refetch();
    await vitals.refetch();
    assert.deepEqual(requested, [
      "/api/deployments/vitals",
      "/api/deployments/vitals?fresh=true",
      "/api/deployments/vitals",
    ]);
  } finally {
    app?.unmount();
    queryClient.clear();
  }
});

test("server vitals panels share one subscription that polls every minute, never restarts an in-flight snapshot and stops after the last panel unmounts", async () => {
  const snapshot = { collectedAt: "2026-09-26T00:00:00Z", targets: [] };
  const requests = [];
  let app;
  const renderer = createRenderer({
    createElement: () => ({}),
    createText: () => ({}),
    createComment: () => ({}),
    insert() {},
    remove() {},
    patchProp() {},
    setText() {},
    setElementText() {},
    parentNode: () => null,
    nextSibling: () => null,
  });
  try {
    globalThis.fetch = (_url, options) =>
      new Promise((settle) =>
        requests.push({ signal: options.signal, settle }),
      );
    queryClient.setQueryData(["servers", "vitals"], snapshot);
    let vitals;
    const Panel = {
      setup() {
        vitals = useServerVitals();
        return () => null;
      },
    };
    // Two panels of one page, as the servers page and its attention list are.
    const shown = reactive({ page: true, attention: true });
    app = renderer.createApp({
      // An element root: the stub renderer has no siblings, so it cannot walk a fragment root to unmount it.
      render: () => h("div", [shown.page ? h(Panel) : null, shown.attention ? h(Panel) : null]),
    });
    app.use(queryPlugin(queryClient));
    app.mount({});
    const query = queryClient
      .getQueryCache()
      .find({ queryKey: ["servers", "vitals"] });
    assert.equal(query.observers.length, 1, "both panels read one subscription on one timer");
    assert.equal(query.observers[0].options.refetchInterval, 60_000);
    const refresh = vitals.refetch();
    await new Promise((resolve) => setImmediate(resolve));
    assert.equal(requests.length, 1);
    // An interval tick fetches through the observer's options, without `cancelRefetch`.
    const tick = () => void query.fetch(query.observers[0].options);
    tick();
    await nextTick();
    assert.equal(
      requests.length,
      1,
      "the timer must not launch a second SSH snapshot",
    );
    assert.equal(
      requests[0].signal.aborted,
      false,
      "the existing snapshot must stay alive",
    );
    requests[0].settle(json({ data: snapshot }));
    await refresh;
    tick();
    await new Promise((resolve) => setImmediate(resolve));
    assert.equal(
      requests.length,
      2,
      "polling resumes once the previous snapshot completes",
    );
    requests[1].settle(json({ data: snapshot }));
    await new Promise((resolve) => setImmediate(resolve));
    shown.page = false;
    await nextTick();
    assert.equal(
      query.observers.length,
      1,
      "another mounted panel still polls",
    );
    shown.attention = false;
    await nextTick();
    assert.equal(query.observers.length, 0, "no panel left polling");
  } finally {
    app?.unmount();
    queryClient.clear();
  }
});
