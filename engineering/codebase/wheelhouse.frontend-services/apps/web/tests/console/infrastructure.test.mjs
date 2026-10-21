import assert from "node:assert/strict";
import { after, afterEach, test } from "node:test";
import { stop } from "esbuild";
import { bundleModule } from "./BundleModule.mjs";

const infra = await bundleModule(`
  export * from './src/domain/infrastructure';
  export * from './src/integration/infrastructure';
  export * from './src/application/infrastructure/EnrollmentForms';
  export * from './src/application/infrastructure/PlacementForms';
`);
const {
  EnrollmentKeySchema,
  EnrollmentRules,
  EnrollmentSchema,
  InfraJobRules,
  NetworkAddress,
  PlacementRules,
  PlacementSchema,
  PolicyAppliedSchema,
  PolicyDiff,
  PolicyReviewSchema,
  PolicyRevertedSchema,
  TailnetRules,
  TailnetStatusSchema,
  UpkeepListingSchema,
  UpkeepRules,
  adminAfterLogin,
  enrollmentApi,
  enrollmentFormOf,
  enrollmentFormSchema,
  enrollmentRequestOf,
  freshServerFormSchema,
  freshServerRequestOf,
  PlacementForms,
  placementFormOf,
  placementFormSchema,
  placementRequestOf,
  placementsApi,
  tailnetApi,
  upkeepApi,
} = infra;
const originalFetch = globalThis.fetch;
afterEach(() => {
  globalThis.fetch = originalFetch;
});
after(() => stop());

const json = (data, status = 200) =>
  new Response(JSON.stringify(data), { status, headers: { "Content-Type": "application/json" } });
const ingress = { scheme: "https", port: null, entryPoints: ["private"], privateEntryPoints: ["private"],
  certResolver: null, pattern: null, probe: "http://127.0.0.1:18080", privateProbe: "http://127.0.0.1:18080" };
// What a products host's kit lays out: enrollment and kit refresh record it on the server.
const kitIngress = { scheme: "https", port: null, entryPoints: ["websecure"], privateEntryPoints: ["private"],
  certResolver: "letsencrypt", pattern: null, probe: "https://127.0.0.1", privateProbe: "http://127.0.0.1:18080" };
const server = (fields = {}) => ({ slug: "ocharo-vps", name: "Ocharo VPS", provider: "contabo", host: "203.0.113.10",
  region: "eu", sshUser: "wheelhouse-deploy", sshPort: 22, ingress, ...fields });
const node = (fields = {}) => ({ id: "n1", name: "ocharo-vps", hostname: "ocharo-vps", addresses: ["100.101.1.2", "fd7a:115c:a1e0::1"],
  tags: ["tag:products"], online: true, lastSeen: "2026-10-19T08:00:00Z", keyExpiryDisabled: true, expires: null,
  created: "2026-10-18T08:00:00Z", os: "linux", authorized: true, serverId: "ocharo-vps", role: "host", ...fields });
const status = (fields = {}) => ({ configured: true, connected: true, reason: null, tailnet: "tail1234.ts.net",
  clientFile: "providers/tailscale/oauth-client.json", nodes: [node(), node({ id: "n2", name: "ocharo-desk", serverId: null,
    role: "site", tags: ["tag:product-site"] })], servers: [{ serverId: "ocharo-vps", nodeId: "n1" }], ...fields });
const hunk = { oldStart: 10, newStart: 10, lines: [
  { kind: "same", old: 10, new: 10, text: '  "grants": [' },
  { kind: "remove", old: 11, new: null, text: '    {"src": ["tag:control"], "dst": ["100.101.1.2"]},' },
  { kind: "add", old: null, new: 11, text: '    {"src": ["tag:control"], "dst": ["100.101.1.2", "100.101.1.3"]},' },
  { kind: "add", old: null, new: 12, text: "    // ocharo-vps" },
  { kind: "same", old: 12, new: 13, text: "  ]," },
] };
const review = (fields = {}) => ({ etag: "\"e1\"", live: "{}", rendered: "{ }", renderedDigest: "a".repeat(64), changed: true,
  diff: [hunk], hosts: [{ serverId: "ocharo-vps", address: "100.101.1.2" }], validation: { ok: true, problems: [] },
  guard: { ok: true, reason: null }, previous: { savedAt: "2026-10-18T10:00:00Z", actor: "max", digest: "b".repeat(64) }, ...fields });
const step = (key, stepStatus, fields = {}) => ({ key, title: key, status: stepStatus, detail: null, startedAt: null,
  completedAt: null, ...fields });
const enrollment = (fields = {}) => ({ kind: "enrollment", serverId: "ocharo-vps", status: "succeeded", id: "e-1", reason: null,
  actor: "max", attempt: 1, createdAt: "2026-10-19T08:00:00Z", startedAt: "2026-10-19T08:00:01Z",
  completedAt: "2026-10-19T08:06:00Z",
  request: { serverId: "ocharo-vps", publicAddress: "203.0.113.10", sshPort: 22, loginUser: "root", admin: "ubuntu",
    adminKey: null, tailnetHostname: "ocharo-vps", expectedHostKey: null },
  facts: { hostKey: "ssh-ed25519 AAAA", hostKeyFingerprint: "SHA256:" + "A".repeat(43), hostKeyConfirmedBy: "tailnet",
    kitDigest: "0123456789abcdef", tailnetAddress: "100.101.1.2", nodeId: "n1", nodeName: "ocharo-vps", publicSshClosed: true },
  mints: [{ keyId: "kXYZ", tags: ["tag:products"], expires: "2026-10-19T08:20:00Z", step: "join" }],
  steps: [step("contact", "succeeded", { startedAt: "2026-10-19T08:00:01Z", completedAt: "2026-10-19T08:00:04Z" })],
  ...fields });
const placement = (fields = {}) => ({ id: "p-1", kind: "placement", serverId: "ocharo-vps", product: "ocharo-platform",
  status: "succeeded", reason: null, actor: "max", createdAt: "2026-10-19T09:00:00Z", startedAt: "2026-10-19T09:00:01Z",
  completedAt: "2026-10-19T09:03:00Z",
  request: { serverId: "ocharo-vps", product: "ocharo-platform", database: true, sites: [{ site: "app", node: "ocharo-desk" }] },
  facts: { databases: ["ocharo-platform-dev", "ocharo-platform-prod"], tailnet: "tail1234.ts.net",
    sites: [{ site: "app", environment: "dev", node: "ocharo-desk-dev", address: "http://ocharo-platform-dev-app:8080",
      joined: true, url: "https://ocharo-desk-dev.tail1234.ts.net", tailnetAddress: "100.101.2.3", online: true },
    { site: "app", environment: "prod", node: "ocharo-desk", address: "http://ocharo-platform-prod-app:8080", joined: true }] },
  mints: [{ keyId: "kS1", tags: ["tag:product-site"], expires: "2026-10-19T09:15:00Z", node: "ocharo-desk" }],
  steps: [step("platform", "succeeded"), step("databases", "succeeded"), step("sites", "succeeded"), step("nodes", "succeeded")],
  ...fields });

test("the tailnet schemas accept the contract's shapes, fill what dev leaves out and refuse garbage", () => {
  const parsed = TailnetStatusSchema.parse(status());
  assert.equal(parsed.prodOnly, false);
  assert.equal(parsed.nodes[1].role, "site");
  const dev = TailnetStatusSchema.parse({ configured: false, connected: false, prodOnly: true,
    reason: "Tailscale runs on prod only", nodes: [], servers: [] });
  assert.deepEqual([dev.tailnet, dev.clientFile, dev.prodOnly], [null, "providers/tailscale/oauth-client.json", true]);
  const sparse = TailnetStatusSchema.parse(status({ nodes: [{ id: "n9", name: "laptop", role: "printer" }] })).nodes[0];
  assert.deepEqual([sparse.role, sparse.addresses, sparse.online, sparse.serverId], ["other", [], false, null]);
  assert.equal(TailnetStatusSchema.safeParse({ configured: "yes", connected: false }).success, false);
  assert.equal(TailnetStatusSchema.safeParse(status({ nodes: {} })).success, false);

  const policy = PolicyReviewSchema.parse(review({ previous: undefined, hosts: undefined }));
  assert.deepEqual([policy.previous, policy.hosts, policy.diff[0].lines.length], [null, [], 5]);
  assert.equal(PolicyReviewSchema.safeParse(review({ diff: [{ ...hunk, lines: [{ kind: "change", old: 1, new: 1, text: "" }] }] })).success, false);
  assert.equal(PolicyReviewSchema.safeParse(review({ validation: null })).success, false);
  assert.equal(PolicyAppliedSchema.parse({ applied: true, digest: "d", etag: null, appliedAt: "2026-10-19T00:00:00Z" }).applied, true);
  assert.equal(PolicyRevertedSchema.safeParse({ reverted: false, digest: "d", etag: null, revertedAt: "x" }).success, false);
});

test("the enrollment and placement schemas accept the contract's shapes and refuse garbage", () => {
  const fresh = EnrollmentSchema.parse({ kind: "enrollment", serverId: "ocharo-vps", status: "not_started", facts: {},
    mints: [], steps: [], oneTimeKey: { publicKey: "ssh-ed25519 AAAA wheelhouse-enroll-ocharo-vps", fingerprint: "SHA256:x" } });
  assert.equal(fresh.oneTimeKey.fingerprint, "SHA256:x");
  assert.deepEqual(EnrollmentSchema.parse({ kind: "enrollment", serverId: "s", status: "queued" }).steps, []);
  const done = EnrollmentSchema.parse(enrollment());
  assert.deepEqual([done.facts.hostKeyConfirmedBy, done.request.adminKey, done.mints[0].keyId], ["tailnet", null, "kXYZ"]);
  assert.equal(EnrollmentSchema.safeParse(enrollment({ status: "done" })).success, false);
  assert.equal(EnrollmentSchema.safeParse(enrollment({ kind: "placement" })).success, false);
  assert.equal(EnrollmentSchema.safeParse(enrollment({ steps: [step("contact", "ok")] })).success, false);
  assert.equal(EnrollmentKeySchema.safeParse({ serverId: "s", publicKey: "ssh-ed25519 AAAA" }).success, false);

  const placed = PlacementSchema.parse(placement());
  assert.equal(placed.facts.sites[1].tailnetAddress, undefined);
  assert.deepEqual(PlacementSchema.parse(placement({ facts: undefined, mints: undefined })).facts, { databases: [], sites: [] });
  assert.equal(PlacementSchema.safeParse(placement({ status: "not_started" })).success, false);
  assert.equal(PlacementSchema.safeParse(placement({ request: null })).success, false);
});

test("the APIs send each write with its action header and decode the envelope", async () => {
  const requests = [];
  globalThis.fetch = async (url, options) => {
    requests.push({ url, options });
    if (url === "/api/tailnet") return json({ data: status() });
    if (url.endsWith("/policy/apply")) return json({ data: { applied: true, digest: "a".repeat(64), etag: "\"e2\"", appliedAt: "2026-10-19T00:00:00Z" } });
    if (url.endsWith("/policy/revert")) return json({ data: { reverted: true, digest: "b", etag: null, revertedAt: "2026-10-19T00:00:00Z" } });
    if (url.endsWith("/enrollment/key")) return json({ data: { serverId: "ocharo vps", publicKey: "ssh-ed25519 AAAA", fingerprint: "SHA256:k" } });
    if (url.includes("/enrollment")) return json({ data: enrollment({ status: "queued" }) });
    return json({ data: options.method === "POST" ? placement({ status: "queued" }) : [placement()] });
  };
  assert.equal((await tailnetApi.getStatus()).value.tailnet, "tail1234.ts.net");
  assert.equal((await tailnetApi.applyPolicy({ etag: "\"e1\"", digest: "a".repeat(64) })).value.etag, "\"e2\"");
  assert.equal((await tailnetApi.revertPolicy({ etag: "\"e1\"" })).ok, true);
  assert.equal((await enrollmentApi.createKey("ocharo vps")).value.fingerprint, "SHA256:k");
  const body = { publicAddress: "203.0.113.10", sshPort: 22, loginUser: "root", admin: "ubuntu", adminKey: null,
    tailnetHostname: "ocharo-vps", expectedHostKey: null };
  assert.equal((await enrollmentApi.start("ocharo-vps", body)).value.status, "queued");
  assert.equal((await placementsApi.listPlacements("ocharo-platform")).value.length, 1);
  assert.equal((await placementsApi.place("ocharo-platform", { server: "ocharo-vps", database: true, sites: [] })).value.status, "queued");
  assert.deepEqual(
    requests.map(({ url, options }) => [url, options.method ?? "GET", options.headers?.get?.("X-Wheelhouse-Action") ?? null]),
    [
      ["/api/tailnet", "GET", null],
      ["/api/tailnet/policy/apply", "POST", "tailnet"],
      ["/api/tailnet/policy/revert", "POST", "tailnet"],
      ["/api/servers/ocharo%20vps/enrollment/key", "POST", "server"],
      ["/api/servers/ocharo-vps/enrollment", "POST", "server"],
      ["/api/products/ocharo-platform/placements", "GET", null],
      ["/api/products/ocharo-platform/placements", "POST", "placement"],
    ],
  );
  assert.deepEqual(JSON.parse(requests[1].options.body), { etag: "\"e1\"", digest: "a".repeat(64) });
  assert.equal(requests[3].options.body ?? null, null, "the key is created without a body");
  assert.deepEqual(JSON.parse(requests[4].options.body), body);

  globalThis.fetch = async () => json({ data: { ...enrollment(), status: "paused" } });
  const refused = await enrollmentApi.getEnrollment("ocharo-vps");
  assert.equal(refused.ok, false);
  assert.equal(refused.failure.code, "protocol");
});

test("a tailnet address is IPv4 in 100.64.0.0/10; enrollment wants any other IP address", () => {
  for (const address of ["100.64.0.1", "100.101.1.2", "100.127.255.255"]) assert.equal(NetworkAddress.isTailnet(address), true, address);
  for (const address of ["100.63.255.255", "100.128.0.1", "10.0.0.1", "100.64.0", "100.064.0.1", "100.64.0.256", "", null, "host.example"])
    assert.equal(NetworkAddress.isTailnet(address), false, String(address));
  assert.deepEqual(NetworkAddress.ipv4(" 203.0.113.10 "), [203, 0, 113, 10]);
  assert.equal(NetworkAddress.isIp("2001:db8::1"), true);
  assert.equal(NetworkAddress.isIp("2001:db8::zz"), false);
  assert.equal(NetworkAddress.isIp("vps.example.net"), false);
  assert.equal(NetworkAddress.isPublic("203.0.113.10"), true);
  assert.equal(NetworkAddress.isPublic("100.101.1.2"), false);
});

test("the Enrollment section shows once one exists, or while the server is reached publicly and is not the rig", () => {
  assert.equal(EnrollmentRules.isVisible(server(), null), true);
  assert.equal(EnrollmentRules.isVisible(server({ host: "100.101.1.2" }), null), false);
  assert.equal(EnrollmentRules.isVisible(server({ host: "100.101.1.2" }), { status: "not_started" }), false);
  assert.equal(EnrollmentRules.isVisible(server({ host: "100.101.1.2" }), { status: "succeeded" }), true);
  assert.equal(EnrollmentRules.isVisible(server({ provider: "local", host: "127.0.0.1" }), null), false);
  assert.equal(EnrollmentRules.isVisible(server({ provider: "local", host: "127.0.0.1" }), { status: "failed" }), true);
  assert.deepEqual(["not_started", "failed", "interrupted", "queued", "running", "succeeded"].map((value) =>
    EnrollmentRules.canStart({ status: value })), [true, true, true, false, false, false]);
  assert.equal(EnrollmentRules.canStart(null), true);
  assert.equal(EnrollmentRules.isRerun({ status: "interrupted" }), true);
  assert.equal(EnrollmentRules.contactSucceeded(enrollment()), true);
  assert.equal(EnrollmentRules.contactSucceeded(enrollment({ steps: [step("contact", "failed")] })), false);
  assert.equal(EnrollmentRules.confirmedBy({ hostKeyConfirmedBy: "owner" }), "confirmed by you");
  assert.equal(EnrollmentRules.authorizeCommand("ssh-ed25519 AAAA c", "root", "203.0.113.10"),
    "echo 'ssh-ed25519 AAAA c' | ssh root@203.0.113.10 'cat >> ~/.ssh/authorized_keys'");
});

test("an enrollment starts from its provider's sign-in account and the server, and a rerun repeats its request", () => {
  const contabo = enrollmentFormOf(server(), null);
  assert.deepEqual(contabo, { publicAddress: "203.0.113.10", sshPort: 22, loginUser: "root", admin: "ubuntu", adminKey: "",
    tailnetHostname: "ocharo-vps", expectedHostKey: "" });
  const ovh = enrollmentFormOf(server({ provider: "ovhcloud", slug: "ovh-1-" }), { status: "not_started", steps: [] });
  assert.deepEqual([ovh.loginUser, ovh.admin, ovh.tailnetHostname], ["ubuntu", "ubuntu", "ovh-1"]);
  assert.equal(enrollmentFormOf(server({ provider: "hetzner" }), null).loginUser, "root");
  assert.equal(enrollmentFormOf(server({ host: "100.101.1.2" }), null).publicAddress, "");
  const rerun = enrollmentFormOf(server({ host: "198.51.100.7" }), enrollment({ status: "failed",
    request: { ...enrollment().request, adminKey: "ssh-ed25519 AAAA me", admin: "max" } }));
  assert.deepEqual([rerun.publicAddress, rerun.admin, rerun.adminKey, rerun.expectedHostKey], ["203.0.113.10", "max", "ssh-ed25519 AAAA me", ""]);

  assert.equal(adminAfterLogin("root", "ubuntu", "max"), "ubuntu", "any account but root is the administrator");
  assert.equal(adminAfterLogin("ubuntu", "root", "ubuntu"), "ubuntu", "root suggests ubuntu again");
  assert.equal(adminAfterLogin("root", "root", "max"), "max", "a typed administrator stays");
  assert.deepEqual(enrollmentRequestOf({ ...contabo, loginUser: " ubuntu ", admin: "max", adminKey: "  ", expectedHostKey: "" }),
    { publicAddress: "203.0.113.10", sshPort: 22, loginUser: "ubuntu", admin: "ubuntu", adminKey: null,
      tailnetHostname: "ocharo-vps", expectedHostKey: null });
  assert.equal(enrollmentRequestOf({ ...contabo, admin: "max" }).admin, "max");
});

test("the enrollment form mirrors the host's patterns", () => {
  const valid = { ...enrollmentFormOf(server(), null), adminKey: "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5 max@laptop",
    expectedHostKey: "SHA256:" + "a".repeat(43) };
  assert.equal(enrollmentFormSchema.safeParse(valid).success, true);
  assert.equal(enrollmentFormSchema.safeParse({ ...valid, publicAddress: "2001:db8::1" }).success, true);
  const refused = (fields) => enrollmentFormSchema.safeParse({ ...valid, ...fields }).error?.issues.map((issue) => issue.path.join("."));
  assert.deepEqual(refused({ publicAddress: "100.101.1.2" }), ["publicAddress"]);
  assert.deepEqual(refused({ publicAddress: "vps.example.net" }), ["publicAddress"]);
  assert.deepEqual(refused({ sshPort: 0 }), ["sshPort"]);
  assert.deepEqual(refused({ loginUser: "Root" }), ["loginUser"]);
  assert.deepEqual(refused({ admin: "root" }), ["admin"]);
  assert.deepEqual(refused({ admin: "wheelhouse-deploy" }), ["admin"]);
  assert.deepEqual(refused({ adminKey: "not a key" }), ["adminKey"]);
  assert.deepEqual(refused({ tailnetHostname: "ocharo-" }), ["tailnetHostname"]);
  assert.deepEqual(refused({ expectedHostKey: "MD5:aa" }), ["expectedHostKey"]);
  assert.equal(enrollmentFormSchema.safeParse({ ...valid, loginUser: "ubuntu", admin: "root" }).success, true,
    "without root the sign-in account is the administrator, whatever the hidden field holds");
});

test("a fresh server registers on its public IP as the deploy account with a products host's ingress", () => {
  const values = { slug: "ocharo-vps", name: "Ocharo VPS", provider: "contabo", region: "eu", publicAddress: " 203.0.113.10 " };
  assert.equal(freshServerFormSchema.safeParse(values).success, true);
  assert.deepEqual(freshServerRequestOf(values), { slug: "ocharo-vps", name: "Ocharo VPS", provider: "contabo",
    host: "203.0.113.10", region: "eu", sshUser: "wheelhouse-deploy", sshPort: 22, ingress: kitIngress });
  assert.equal(freshServerFormSchema.safeParse({ ...values, provider: "local" }).success, false);
  assert.equal(freshServerFormSchema.safeParse({ ...values, publicAddress: "100.101.1.2" }).success, false);
  assert.equal(freshServerFormSchema.safeParse({ ...values, slug: "Ocharo" }).success, false);
});

test("the policy diff draws hunk headers, markers and counts", () => {
  assert.equal(PolicyDiff.header(hunk), "@@ -10,3 +10,4 @@");
  assert.deepEqual(PolicyDiff.counts([hunk, hunk]), { added: 4, removed: 2 });
  assert.equal(PolicyDiff.summary([hunk]), "2 lines added, 1 removed");
  assert.equal(PolicyDiff.summary([{ ...hunk, lines: hunk.lines.filter((line) => line.kind === "remove") }]), "1 line removed");
  assert.equal(PolicyDiff.summary([]), "No change");
  assert.deepEqual(["same", "add", "remove"].map(PolicyDiff.marker), [" ", "+", "-"]);
});

test("the tailnet panel's state, a server's node, and when a policy can be applied or reverted", () => {
  assert.equal(TailnetRules.connectionOf(TailnetStatusSchema.parse(status())), "connected");
  assert.equal(TailnetRules.connectionOf(TailnetStatusSchema.parse(status({ connected: false }))), "refused");
  assert.equal(TailnetRules.connectionOf(TailnetStatusSchema.parse(status({ connected: false, configured: false }))), "not-configured");
  assert.equal(TailnetRules.connectionOf(TailnetStatusSchema.parse(status({ connected: false, configured: false, prodOnly: true }))), "prod-only");
  assert.equal(TailnetRules.clientPath({ clientFile: "providers/tailscale/oauth-client.json" }),
    "<runner root>/providers/tailscale/oauth-client.json");
  const read = TailnetStatusSchema.parse(status());
  assert.equal(TailnetRules.nodeOf(read, "ocharo-vps").id, "n1");
  assert.equal(TailnetRules.nodeOf(read, "hel1"), null);
  assert.equal(TailnetRules.nodeOf(undefined, "ocharo-vps"), null);
  assert.equal(TailnetRules.addressOf({ addresses: ["fd7a:115c:a1e0::1", "100.101.1.2"] }), "100.101.1.2");
  assert.equal(TailnetRules.addressOf({ addresses: [] }), null);
  assert.equal(TailnetRules.canApply(review()), true);
  assert.equal(TailnetRules.canApply(review({ changed: false })), false);
  assert.equal(TailnetRules.canApply(review({ validation: { ok: false, problems: ["test failed"] } })), false);
  assert.equal(TailnetRules.canApply(review({ guard: { ok: false, reason: "no control path" } })), false);
  assert.equal(TailnetRules.canApply(null), false);
  assert.equal(TailnetRules.canRevert(review()), true);
  assert.equal(TailnetRules.canRevert(review({ previous: null })), false);
});

test("jobs poll every two seconds while queued or running and say when a run finished", () => {
  assert.deepEqual(["not_started", "queued", "running", "succeeded", "failed", "interrupted", undefined, null]
    .map(InfraJobRules.pollInterval), [false, 2000, 2000, false, false, false, false, false]);
  assert.equal(InfraJobRules.anyActive([{ status: "succeeded" }, { status: "queued" }]), true);
  assert.equal(InfraJobRules.anyActive([{ status: "failed" }]), false);
  assert.equal(InfraJobRules.anyActive(undefined), false);
  assert.equal(InfraJobRules.hasFinished("running", "succeeded"), true);
  assert.equal(InfraJobRules.hasFinished("queued", "failed"), true);
  assert.equal(InfraJobRules.hasFinished("queued", "running"), false);
  assert.equal(InfraJobRules.hasFinished(undefined, "succeeded"), false, "a job first read finished refreshes nothing");
  assert.equal(InfraJobRules.hasFinished("running", undefined), false);
  assert.deepEqual([InfraJobRules.canRetry("failed"), InfraJobRules.canRetry("interrupted"), InfraJobRules.canRetry("succeeded")],
    [true, true, false]);
  assert.deepEqual(["queued", "succeeded", "failed", "interrupted", "not_started"].map(InfraJobRules.state),
    ["deploying", "succeeded", "failed", "degraded", "none"]);
  assert.equal(InfraJobRules.word("not_started"), "Not started");
  assert.equal(InfraJobRules.shortDigest("0123456789abcdef"), "0123456789ab");
  assert.equal(InfraJobRules.shortDigest(undefined), "");
});

test("a step's duration: finished, ticking while it runs, none before it starts", () => {
  const started = "2026-10-19T08:00:00Z";
  assert.equal(InfraJobRules.stepSeconds(step("a", "succeeded", { startedAt: started, completedAt: "2026-10-19T08:01:05Z" })), 65);
  assert.equal(InfraJobRules.stepSeconds(step("a", "running", { startedAt: started }), Date.parse(started) + 7_000), 7);
  assert.equal(InfraJobRules.stepSeconds(step("a", "pending")), null);
  assert.equal(InfraJobRules.stepSeconds(step("a", "failed", { startedAt: started })), null);
  assert.equal(InfraJobRules.hasRunningStep([step("a", "succeeded"), step("b", "running")]), true);
});

// What Wheelhouse reads from deploy.yml: Ocharo Studio (one service, SQLite, a public app site) and Ocharo Platform
// (desk and jobs need PostgreSQL; desk's app site is public), plus a private admin site for the node rows.
const studio = { commit: "a".repeat(40), problem: null, services: [{ service: "studio", needs: [] }],
  sites: [{ name: "app", exposure: "public", primary: true }] };
const platform = { commit: "b".repeat(40), problem: null,
  services: [{ service: "desk", needs: ["postgres"] }, { service: "jobs", needs: ["postgres"] }, { service: "image-worker", needs: [] }],
  sites: [{ name: "app", exposure: "public", primary: true }] };
const withAdmin = { ...platform, sites: [...platform.sites, { name: "admin", exposure: "private", primary: false }] };

test("a placement plans only what deploy.yml calls for and names nodes after the product and site", () => {
  assert.deepEqual(PlacementRules.planOf(studio), { databases: [], sites: [], publicSites: ["app"] });
  assert.equal(PlacementRules.hasWork(PlacementRules.planOf(studio)), false, "Studio needs nothing placed");
  assert.deepEqual(PlacementRules.planOf(platform), { databases: ["desk", "jobs"], sites: [], publicSites: ["app"] });
  assert.equal(PlacementRules.hasWork(PlacementRules.planOf(platform)), true);
  assert.deepEqual(PlacementRules.planOf(withAdmin).sites, ["admin"]);
  assert.equal(PlacementRules.defaultNode("ocharo-platform", "admin"), "ocharo-platform-admin");
  assert.equal(PlacementRules.devNode("ocharo-desk"), "ocharo-desk-dev");
  // A site keeps the node a host already registered for it there; elsewhere it takes <product>-<site>.
  const placed = [PlacementSchema.parse(placement())];
  assert.equal(PlacementRules.nodeFor("ocharo-platform", "app", "ocharo-vps", placed), "ocharo-desk");
  assert.equal(PlacementRules.nodeFor("ocharo-platform", "app", "other-vps", placed), "ocharo-platform-app");
  assert.equal(PlacementRules.nodeFor("ocharo-platform", "admin", "ocharo-vps", placed), "ocharo-platform-admin");
  assert.equal(PlacementRules.nodeFor("ocharo-platform", "admin", "ocharo-vps"), "ocharo-platform-admin");
  assert.deepEqual(PlacementRules.eligibleServers([server({ slug: "rig", provider: "local" }), server()]).map((item) => item.slug),
    ["ocharo-vps"]);
  assert.equal(PlacementRules.nodeProblem("postgres"), "postgres is kept for the host.");
  assert.notEqual(PlacementRules.nodeProblem("Desk"), null);
  assert.equal(PlacementRules.nodeProblem("ocharo-desk"), null);
  assert.deepEqual(PlacementRules.placedSites(PlacementSchema.parse(placement())).map((site) => site.environment), ["prod", "dev"]);
});

test("the placement form starts with everything the manifest calls for ticked and sends only that", () => {
  const plan = PlacementRules.planOf(withAdmin);
  const values = placementFormOf(plan, [server({ slug: "rig", provider: "local" }), server()]);
  assert.deepEqual(values, { server: "ocharo-vps", database: true, sites: ["admin"] });
  assert.equal(placementFormOf(plan, [server({ provider: "local" })]).server, "");
  assert.deepEqual(placementFormOf(PlacementRules.planOf({ ...studio, sites: [{ name: "ops", exposure: "private", primary: false }] }),
    [server()]), { server: "ocharo-vps", database: false, sites: ["ops"] }, "no database box for a product without PostgreSQL");
  assert.equal(placementFormSchema.safeParse(values).success, true);
  assert.equal(placementFormSchema.safeParse({ ...values, sites: [] }).success, true, "databases alone");
  const paths = (fields) => placementFormSchema.safeParse({ ...values, ...fields }).error?.issues.map((issue) => issue.path.join("."));
  assert.deepEqual(paths({ database: false, sites: [] }), ["sites"]);
  assert.deepEqual(paths({ server: "" }), ["server"]);
  assert.equal(PlacementForms.hasChoice({ database: false, sites: [] }), false);
  assert.equal(PlacementForms.hasChoice({ database: false, sites: ["admin"] }), true);
  const two = { ...plan, sites: ["admin", "ops"] };
  assert.deepEqual(PlacementForms.toggled(two, ["ops"], "admin", true), ["admin", "ops"], "in the plan's order");
  assert.deepEqual(PlacementForms.toggled(two, ["admin", "ops"], "admin", false), ["ops"]);
  const node = (site) => `ocharo-platform-${site}`;
  assert.deepEqual(placementRequestOf(values, plan, node),
    { server: "ocharo-vps", database: true, sites: [{ site: "admin", node: "ocharo-platform-admin" }] });
  // Nothing beyond the plan reaches the host: no stray site, no databases a product does not need.
  assert.deepEqual(placementRequestOf({ ...values, sites: ["admin", "app"] }, plan, node).sites.map((site) => site.site), ["admin"]);
  assert.equal(placementRequestOf({ server: "ocharo-vps", database: true, sites: [] }, PlacementRules.planOf(studio), node).database, false);
});

const upkeepJob = (fields = {}) => ({ id: "u1", kind: "upkeep", serverId: "ocharo-vps", status: "succeeded", reason: null,
  actor: "sulton-max", createdAt: "2026-10-20T08:00:00Z", startedAt: "2026-10-20T08:00:01Z",
  completedAt: "2026-10-20T08:01:00Z", facts: { kitDigest: "c".repeat(64), publicAddress: "203.0.113.10",
    addressSource: "enrollment" }, steps: [], ...fields });

test("an enrolled host's kit section knows whether it runs this console's kit and where its address came from", () => {
  const listing = UpkeepListingSchema.parse({ serverId: "ocharo-vps", kit: "c".repeat(64),
    jobs: [upkeepJob({ id: "u2", status: "failed", reason: "Port 80 does not redirect to https (404)", facts: {} }), upkeepJob()] });
  assert.equal(UpkeepRules.latest(listing).id, "u2");
  assert.equal(UpkeepRules.hostKit(listing), "c".repeat(64));
  assert.equal(UpkeepRules.isCurrent(listing), true);
  assert.equal(UpkeepRules.isCurrent({ ...listing, kit: "d".repeat(64) }), false);
  assert.equal(UpkeepRules.isCurrent({ serverId: "ocharo-vps", kit: "c".repeat(64), jobs: [] }), false);
  assert.equal(UpkeepRules.canStart(listing), true);
  assert.equal(UpkeepRules.canStart({ ...listing, jobs: [upkeepJob({ status: "running" })] }), false);
  assert.equal(UpkeepRules.sourceWord("enrollment"), "from enrollment");
  assert.equal(UpkeepRules.sourceWord(undefined), null);
  // Only an enrolled host, reached on its tailnet address, shows it; never the local rig or a host still public.
  assert.equal(UpkeepRules.isVisible(server({ host: "100.101.7.8" })), true);
  assert.equal(UpkeepRules.isVisible(server()), false);
  assert.equal(UpkeepRules.isVisible(server({ host: "100.101.7.8", provider: "local" })), false);
  assert.equal(UpkeepListingSchema.parse({ serverId: "x", kit: null }).jobs.length, 0);
  assert.equal(UpkeepListingSchema.parse({ serverId: "x", kit: null, jobs: [upkeepJob({ facts: { addressSource: "dns" } })] })
    .jobs[0].facts.addressSource, undefined);
});

test("the kit refresh reads with GET and starts with the server action header", async () => {
  const requests = [];
  globalThis.fetch = async (url, options) => {
    requests.push([url, options.method ?? "GET", options.headers?.get?.("X-Wheelhouse-Action") ?? null]);
    return json({ data: options.method === "POST" ? upkeepJob({ status: "queued" }) : { serverId: "ocharo-vps", kit: null, jobs: [] } });
  };
  assert.equal((await upkeepApi.list("ocharo vps")).value.jobs.length, 0);
  assert.equal((await upkeepApi.start("ocharo-vps")).value.status, "queued");
  assert.deepEqual(requests, [["/api/servers/ocharo%20vps/upkeep", "GET", null], ["/api/servers/ocharo-vps/upkeep", "POST", "server"]]);
});
