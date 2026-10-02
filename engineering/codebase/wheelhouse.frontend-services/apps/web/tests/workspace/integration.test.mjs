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
      export { clearHttpSession } from './src/integration/common';
      export { useServerVitals } from './src/application/servers/useServerVitals';
      export { queryClient, queryPlugin } from './src/bootstrap/query';
      export { createRenderer, nextTick } from 'vue';
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
  clearHttpSession,
  useServerVitals,
  queryClient,
  queryPlugin,
  createRenderer,
  nextTick,
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
  lifecycle: "building",
  repository: { name: "owner/repository", url: "https://github.com/owner/repository", defaultBranch: "main" },
  release: { asset: "test-release.tar.gz", workflow: null, images: [{ service: "api", image: "ghcr.io/owner/test/api" }] },
  iconUrl: "/api/products/test/icon",
  environments: [
    { name: "dev", sites: [{ name: "app", url: "https://dev.example.com", exposure: "public" }],
      secrets: { vault: "pilot-vault", namespace: "test-dev" } },
    { name: "prod", sites: [], secrets: null },
  ],
};

test("serializes a lifecycle write once with its action and same-origin cookie credentials", async () => {
  let captured;
  globalThis.fetch = async (url, options) => {
    captured = { url, options };
    return json({ data: { ...product, lifecycle: "live" } });
  };
  const result = await productsApi.updateLifecycle(product.slug, "live");
  assert.equal(result.ok, true);
  assert.equal(result.value.lifecycle, "live");
  assert.deepEqual(result.value.environments, product.environments);
  assert.equal(captured.url, "/api/products/test/lifecycle");
  assert.equal(captured.options.method, "PUT");
  assert.equal(captured.options.credentials, "same-origin");
  assert.equal(captured.options.headers.get("X-Wheelhouse-Action"), "lifecycle");
  assert.deepEqual(JSON.parse(captured.options.body), { lifecycle: "live" });
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
    skipTestPass: true,
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
          { id: "legacy", product: "foreverpin", environment: "test",
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
    targets.value.map((item) => [item.acceptsCandidates, item.needsConfirmation]),
    [[true, false], [false, false]],
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

test("pages the audit trail and names each action and chain break in words", async () => {
  const requests = [];
  globalThis.fetch = async (url) => {
    requests.push(url);
    return json({ data: url.includes("verification")
      ? { intact: false, entries: 3, brokenSequence: 2, reason: "HashMismatch" }
      : [{ sequence: 3, occurredAt: "2026-09-28T10:00:00Z", actor: "max", action: "deployment.start",
          subject: "foreverpin-dev", outcome: "succeeded", detail: "v1", reason: null }] });
  };
  assert.equal((await auditApi.list(100, 4)).value[0].sequence, 3);
  assert.equal((await auditApi.verify()).value.brokenSequence, 2);
  assert.deepEqual(requests, ["/api/audit?limit=100&before=4", "/api/audit/verification"]);
  assert.equal(AuditExtensions.label("vault.secret.set"), "Set a secret");
  assert.equal(AuditExtensions.label("future.action"), "future.action");
  assert.equal(AuditExtensions.inArea("build.request", AuditArea.Deployments), true);
  assert.equal(AuditExtensions.inArea("vault.token.mint", AuditArea.Products), false);
  assert.equal(AuditExtensions.breakLabel("HashMismatch"), "an entry was edited");
});

test("lines environments up dev to prod and offers each published release to the next one", () => {
  const target = (environment) => ({ id: `pin-${environment}`, product: "pin", environment, serverId: "local",
    provider: "Local", host: "127.0.0.1", acceptsCandidates: environment === "dev", needsConfirmation: false,
    requiresTestPass: environment === "prod" });
  const state = (release, kind, versions) => ({ targetId: "", project: "", condition: "ready", active: null,
    current: { id: "job", release, kind, versions } });
  const releases = [{ id: "pin-v1-1", product: "pin", release: "v1.1", kind: "release", prerelease: false,
    publishedAt: "2026-09-28T00:00:00Z", provider: "github" }];
  const comparison = compareEnvironments(
    [target("prod"), target("dev"), target("test")],
    [
      state("v1.0", "release", { api: { version: "1.0", changedIn: "v1.0" } }),
      state("sha-abc", "candidate", { api: { version: "1.1+abc", changedIn: "sha-abc" } }),
      state("v1.1", "release", { api: { version: "1.1", changedIn: "v1.1" } }),
    ],
    releases,
  );
  assert.deepEqual(comparison.columns.map((column) => column.environment), ["dev", "test", "prod"]);
  assert.equal(comparison.columns[0].promotion, null); // a commit's build never leaves dev
  assert.deepEqual(comparison.columns[1].promotion,
    { targetId: "pin-prod", environment: "prod", bundleId: "pin-v1-1", release: "v1.1" });
  assert.deepEqual(comparison.rows[0].pending, [true, true, false]);
  const unread = compareEnvironments([target("test"), target("prod")],
    [state("v1.1", "release", {}), undefined], releases);
  assert.equal(unread.columns[0].promotion, null);
  assert.equal(unread.columns[1].read, false);
});

test("reads vitals history and traces one server's host figures from its first target", async () => {
  const sample = (targetId, serverId, sampledAt, memoryPercent) => ({ targetId, serverId, sampledAt, readable: true,
    loadPercent: null, memoryPercent, diskPercent: 40, containers: 2, healthyContainers: 2, restarts: 0 });
  let requested;
  globalThis.fetch = async (url) => {
    requested = url;
    return json({ data: [
      sample("pin-dev", "local", "2026-09-28T10:00:00Z", 50),
      sample("pin-test", "local", "2026-09-28T10:00:00Z", 50),
      sample("pin-dev", "local", "2026-09-28T10:05:00Z", null),
      sample("pin-dev", "local", "2026-09-28T10:10:00Z", 70),
      sample("other-prod", "vps", "2026-09-28T10:10:00Z", 10),
    ] });
  };
  const result = await serversApi.getVitalsHistory(24);
  assert.equal(requested, "/api/deployments/vitals/history?hours=24");
  assert.deepEqual(hostTrend(result.value, "local", "memoryPercent").map((point) => point.value), [50, 70]);
  assert.deepEqual(hostTrend(result.value, "vps", "diskPercent").map((point) => point.value), [40]);
  assert.deepEqual(hostTrend(result.value, "missing", "memoryPercent"), []);
});

test("accepts an explicit empty logout success", async () => {
  globalThis.fetch = async () => new Response(null, { status: 204 });
  assert.deepEqual(await authApi.signOut(), { ok: true, value: undefined });
});

test("rejects a missing management envelope and malformed consumed product fields", async () => {
  for (const payload of [
    [product],
    { data: [{ ...product, lifecycle: "invented" }] },
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
        host: {
          cpus: null,
          load: null,
          memoryTotalBytes: null,
          memoryAvailableBytes: null,
          uptimeSeconds: null,
          disks: [],
        },
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
  const pending = productsApi.listProducts();
  await new Promise((resolve) => setTimeout(resolve, 0));
  clearHttpSession();
  const result = await pending;
  assert.equal(result.ok, false);
  assert.equal(result.failure.code, "cancelled");
  settle(json({ data: [product] }));
});

test("server vitals polling preserves an in-flight snapshot and stops after the last panel unmounts", async () => {
  const originalInterval = globalThis.setInterval;
  const originalClearInterval = globalThis.clearInterval;
  const originalDocument = Object.getOwnPropertyDescriptor(
    globalThis,
    "document",
  );
  const snapshot = { collectedAt: "2026-09-26T00:00:00Z", targets: [] };
  const requests = [];
  let poll;
  let timers = 0;
  let cleared = 0;
  const apps = [];
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
    Object.defineProperty(globalThis, "document", {
      configurable: true,
      value: { visibilityState: "visible" },
    });
    globalThis.setInterval = (callback, delay) => {
      assert.equal(delay, 60_000);
      timers++;
      poll = callback;
      return 123;
    };
    globalThis.clearInterval = () => cleared++;
    globalThis.fetch = (_url, options) =>
      new Promise((settle) =>
        requests.push({ signal: options.signal, settle }),
      );
    queryClient.setQueryData(["servers", "vitals"], snapshot);
    let vitals;
    for (let index = 0; index < 2; index++) {
      const app = renderer.createApp({
        setup() {
          vitals = useServerVitals();
          return () => null;
        },
      });
      app.use(queryPlugin(queryClient));
      app.mount({});
      apps.push(app);
    }
    assert.equal(timers, 1);
    const refresh = vitals.refetch();
    await new Promise((resolve) => setImmediate(resolve));
    assert.equal(requests.length, 1);
    poll();
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
    poll();
    await new Promise((resolve) => setImmediate(resolve));
    assert.equal(
      requests.length,
      2,
      "polling resumes once the previous snapshot completes",
    );
    requests[1].settle(json({ data: snapshot }));
    await new Promise((resolve) => setImmediate(resolve));
    apps.pop().unmount();
    assert.equal(
      cleared,
      0,
      "another mounted panel still owns the shared timer",
    );
    apps.pop().unmount();
    assert.equal(cleared, 1);
  } finally {
    for (const app of apps) app.unmount();
    queryClient.clear();
    globalThis.setInterval = originalInterval;
    globalThis.clearInterval = originalClearInterval;
    if (originalDocument)
      Object.defineProperty(globalThis, "document", originalDocument);
    else delete globalThis.document;
  }
});
