import assert from "node:assert/strict";
import { after, test } from "node:test";
import { stop } from "esbuild";
import { bundleModule } from "./BundleModule.mjs";

// The real Files migration rules, the wire schemas (old records and new) and the API calls the dialog makes.
const {
  ControlOperation, ServiceControls, FilesMigrations, DeploymentItemSchema, DeploymentDetailSchema, DeploymentJobSchema,
  FilesMigrationManifestSchema, DeploymentStates, JobStatus, StepStatus, deploymentsApi,
} = await bundleModule(`
  export { ControlOperation, ServiceControls, FilesMigrations } from './src/domain/deployments/controls';
  export { DeploymentStates, JobStatus, StepStatus } from './src/domain/deployments';
  export { deploymentsApi } from './src/integration/deployments';
  export { DeploymentJobSchema, FilesMigrationManifestSchema } from './src/integration/deployments/schemas';
  export { DeploymentItemSchema, DeploymentDetailSchema } from './src/integration/deployments/historySchemas';
`);
after(() => stop());

const URL_ = "https://files.dev.ocharo.app/";
const sources = [
  { id: "brand-media", kind: "volume-folder", wave: 1, repository: "owner/a" },
  { id: "desk-media", kind: "postgres-column", wave: 2, repository: "owner/a" },
  { id: "studio-media", kind: "volume-folder", wave: 2, repository: "owner/b" },
  { id: "loose", kind: "sqlite-column", wave: null, repository: "owner/b" },
];
const ask = (over = {}) => ({ step: "plan", scope: { kind: "all" }, filesTarget: "files-dev", filesUrl: URL_, ...over });
const run = {
  import: "imp-7",
  results: { "brand-media": { files: 12, bytes: 3400, skipped: 2, failed: 0 }, "desk-media": { files: 5, bytes: 90, skipped: 0, failed: 1 } },
};
const itemBase = { id: "j1", status: "succeeded", outcome: "succeeded", state: "succeeded", attention: "none", submittedAt: "2026-11-08T10:00:00Z" };

test("the migration is offered on a dev environment of a product, never prod nor Wheelhouse itself", () => {
  const dev = { environment: "dev", product: "ocharo" };
  assert.equal(FilesMigrations.offers(dev, "wheelhouse"), true);
  assert.equal(FilesMigrations.offers({ ...dev, environment: "prod" }, "wheelhouse"), false);
  assert.equal(FilesMigrations.offers({ environment: "dev", product: "wheelhouse" }, "wheelhouse"), false);
  assert.equal(FilesMigrations.offers(null, "wheelhouse"), false);
});

test("waves list ascending and a scope picks all, one wave or the ids", () => {
  assert.deepEqual(FilesMigrations.waves(sources), [1, 2]);
  assert.equal(FilesMigrations.pick(sources, { kind: "all" }).length, 4);
  assert.deepEqual(FilesMigrations.pick(sources, { kind: "wave", wave: 2 }).map((s) => s.id), ["desk-media", "studio-media"]);
  assert.deepEqual(FilesMigrations.pick(sources, { kind: "sources", ids: ["loose", "brand-media"] }).map((s) => s.id), ["brand-media", "loose"]);
});

test("each step sends its own body: a plan never an import, a push one only to resume, a verify always", () => {
  assert.deepEqual(FilesMigrations.body(ask({ import: "imp-7" })), { step: "plan", filesTarget: "files-dev", filesUrl: URL_ });
  assert.deepEqual(FilesMigrations.body(ask({ step: "push", scope: { kind: "wave", wave: 2 } })),
    { step: "push", wave: 2, filesTarget: "files-dev", filesUrl: URL_ });
  assert.deepEqual(FilesMigrations.body(ask({ step: "push", scope: { kind: "wave", wave: 2 }, import: " imp-7 " })),
    { step: "push", wave: 2, filesTarget: "files-dev", filesUrl: URL_, import: "imp-7" });
  assert.deepEqual(FilesMigrations.body(ask({ step: "verify", scope: { kind: "sources", ids: ["brand-media"] }, import: "imp-7" })),
    { step: "verify", sources: ["brand-media"], filesTarget: "files-dev", filesUrl: URL_, import: "imp-7" });
  assert.equal(FilesMigrations.isComplete(ask({ step: "verify" }), 2), false, "a verify needs its import");
  assert.equal(FilesMigrations.isComplete(ask({ step: "verify", import: "imp-7" }), 2), true);
  assert.equal(FilesMigrations.isComplete(ask(), 0), false, "no source picked");
  assert.equal(FilesMigrations.isComplete(ask({ filesUrl: "" }), 2), false);
  assert.equal(FilesMigrations.isImport("imp-7"), true);
  assert.equal(FilesMigrations.isImport("imp 7"), false);
});

test("only a push and a verify ask a confirmation, and it names the source count and the files host", () => {
  assert.deepEqual(FilesMigrations.steps.map((step) => [step, FilesMigrations.confirms(step)]),
    [["plan", false], ["push", true], ["verify", true]]);
  assert.equal(FilesMigrations.question("push", 3, URL_), "Push 3 sources to Files at files.dev.ocharo.app?");
  assert.equal(FilesMigrations.question("verify", 1, URL_), "Verify 1 source against Files at files.dev.ocharo.app?");
  assert.match(FilesMigrations.consequence("push", "ocharo-dev", URL_), /upload into an import on Files at files\.dev\.ocharo\.app/);
});

test("the import prefills from the newest files-migration job of the target that kept one", () => {
  const job = (targetId, kind, imported) => ({ targetId, operation: { kind }, filesMigration: imported === undefined ? null : { import: imported } });
  const items = [
    job("ocharo-dev", "files-migration", null),
    job("other-dev", "files-migration", "imp-other"),
    job("ocharo-dev", "run", "imp-run"),
    job("ocharo-dev", "files-migration", "imp-9"),
    job("ocharo-dev", "files-migration", "imp-3"),
  ];
  assert.equal(FilesMigrations.latestImport(items, "ocharo-dev"), "imp-9");
  assert.equal(FilesMigrations.latestImport(items, "nowhere-dev"), null);
});

test("results list in the order the job ran, with totals", () => {
  assert.deepEqual(FilesMigrations.rows(run, ["desk-media", "brand-media"]).map((row) => row.source), ["desk-media", "brand-media"]);
  assert.deepEqual(FilesMigrations.total(run), { files: 17, bytes: 3490, skipped: 2, failed: 1 });
  assert.deepEqual(FilesMigrations.rows(null), []);
  assert.equal(FilesMigrations.counts({ files: 1, bytes: 2048 }), "1 file · 2 KiB");
});

test("a history item reads old and new records: the step and sources, the results, and a cancelled job", () => {
  const old = DeploymentItemSchema.parse(itemBase);
  assert.equal(old.filesMigration, null, "a record from before the control carries none");
  const fresh = DeploymentItemSchema.parse({
    ...itemBase,
    operation: { kind: "files-migration", step: "push", sources: ["brand-media", "desk-media"] },
    filesMigration: run,
  });
  assert.deepEqual(fresh.operation.services, [], "the operation names no service");
  assert.equal(fresh.operation.step, "push");
  assert.deepEqual(fresh.operation.sources, ["brand-media", "desk-media"]);
  assert.deepEqual(fresh.filesMigration, run);
  assert.equal(ServiceControls.labelOf(fresh.operation), "Files migration: push");
  assert.equal(ServiceControls.labelOf({ kind: ControlOperation.FilesMigration, services: [] }), "Files migration");

  const stopped = DeploymentItemSchema.parse({ ...itemBase, status: "cancelled", outcome: "cancelled", state: "not_applied", substate: "cancelled",
    attention: "none", operation: { kind: "files-migration", services: null, step: "verify" }, filesMigration: { import: "imp-7", results: {} } });
  assert.equal(stopped.status, "cancelled");
  assert.equal(stopped.outcome, "cancelled");
  const reading = DeploymentStates.of(stopped);
  assert.deepEqual([reading.word, reading.detail], ["Not applied", "cancelled"]);
  assert.deepEqual(stopped.operation.services, [], "a null service list reads empty");

  const unreadable = DeploymentItemSchema.parse({ ...itemBase, filesMigration: { results: { a: { files: "many" } } } });
  assert.equal(unreadable.filesMigration, null, "a malformed result never fails the item");
  const partial = DeploymentItemSchema.parse({ ...itemBase, filesMigration: { results: { a: { files: 3, bytes: 9 } } } });
  assert.deepEqual(partial.filesMigration, { import: null, results: { a: { files: 3, bytes: 9, skipped: 0, failed: 0 } } });
});

test("a cancelled status reads as not applied, never failed, even without the API's reading", () => {
  const reading = DeploymentStates.legacy(JobStatus.Cancelled);
  assert.deepEqual([reading.state, reading.substate, reading.attention], ["not_applied", "cancelled", "none"]);
  const unknown = DeploymentItemSchema.parse({ ...itemBase, status: "cancelled", outcome: "cancelled", state: undefined, attention: undefined });
  assert.equal(unknown.state, "not_applied");
  assert.equal(unknown.attention, "none");
});

test("the detail carries the migration's results, and a job read carries its steps, import and cancelled status", () => {
  const detail = DeploymentDetailSchema.parse({
    deployment: { ...itemBase, operation: { kind: "files-migration", step: "push", sources: ["brand-media"] }, filesMigration: run },
    steps: [{ name: "brand-media", status: "succeeded", detail: "12 files · 3400 bytes" }, { name: "desk-media", status: "cancelled" }],
  });
  assert.deepEqual(detail.deployment.filesMigration, run);
  assert.equal(detail.steps[1].status, StepStatus.Cancelled);
  assert.equal(DeploymentDetailSchema.parse({ deployment: itemBase }).deployment.filesMigration, null, "an older detail reads as before");

  const job = DeploymentJobSchema.parse({
    id: "j1", status: "cancelled", operation: { kind: "files-migration", step: "push", sources: ["brand-media"] },
    steps: [{ name: "brand-media", status: "running", detail: "4 files · 120 bytes" }, { name: "desk-media", status: "cancelled" }],
    filesMigration: { import: "imp-7", results: {} },
  });
  assert.equal(job.status, JobStatus.Cancelled);
  assert.equal(job.steps[0].detail, "4 files · 120 bytes");
  assert.deepEqual(job.filesMigration, { import: "imp-7", results: {} });
  assert.equal(DeploymentJobSchema.parse({ id: "d1", status: "succeeded" }).filesMigration, undefined, "a deploy carries none");
  assert.equal(DeploymentJobSchema.parse({ id: "d1", status: "succeeded", filesMigration: 3 }).filesMigration, null);
});

test("the manifest read lists sources and the Files targets with their addresses", () => {
  const manifest = FilesMigrationManifestSchema.parse({
    tenant: "ocharo",
    sources: [{ id: "brand-media", kind: "volume-folder", wave: 1, repository: "owner/a" }, { id: "loose", kind: "sqlite-column" }],
    filesTargets: [{ target: "files-dev", urls: ["https://files.dev.ocharo.app/", "https://files.ocharo.app/"] }],
  });
  assert.deepEqual(manifest.sources[1], { id: "loose", kind: "sqlite-column", wave: null, repository: null });
  assert.equal(manifest.filesTargets[0].urls.length, 2);
  assert.deepEqual(FilesMigrationManifestSchema.parse({ tenant: "t", sources: [] }).filesTargets, []);
});

/** Replaces `fetch` for one test and records each call. */
async function withFetch(answers, body) {
  const calls = [];
  const original = globalThis.fetch;
  globalThis.fetch = async (url, options = {}) => {
    calls.push({ url, method: options.method ?? "GET", action: options.headers?.get?.("X-Wheelhouse-Action") ?? null,
      body: options.body ? JSON.parse(options.body) : null });
    return new Response(JSON.stringify({ data: answers(url) }), { status: 200, headers: { "Content-Type": "application/json" } });
  };
  try {
    await body(calls);
  } finally {
    globalThis.fetch = original;
  }
}

test("each step posts its body with the control action, and the manifest is a plain read", async () => {
  const queued = { id: "j1", targetId: "ocharo-dev", status: "queued", operation: { kind: "files-migration", step: "push", sources: ["a"] } };
  await withFetch((url) => (url.endsWith("/manifest") ? { tenant: "ocharo", sources: [], filesTargets: [] } : queued), async (calls) => {
    assert.equal((await deploymentsApi.filesMigrationManifest("ocharo-dev")).ok, true);
    for (const step of ["plan", "push", "verify"]) {
      const result = await deploymentsApi.startFilesMigration("ocharo-dev", FilesMigrations.body(ask({ step, import: step === "plan" ? "" : "imp-7" })));
      assert.equal(result.ok, true);
      assert.equal(result.value.operation.kind, ControlOperation.FilesMigration);
    }
    assert.deepEqual(calls.map((call) => [call.url, call.method, call.action]), [
      ["/api/deployments/targets/ocharo-dev/files-migrations/manifest", "GET", null],
      ["/api/deployments/targets/ocharo-dev/files-migrations", "POST", "control"],
      ["/api/deployments/targets/ocharo-dev/files-migrations", "POST", "control"],
      ["/api/deployments/targets/ocharo-dev/files-migrations", "POST", "control"],
    ]);
    assert.deepEqual(calls.slice(1).map((call) => call.body), [
      { step: "plan", filesTarget: "files-dev", filesUrl: URL_ },
      { step: "push", filesTarget: "files-dev", filesUrl: URL_, import: "imp-7" },
      { step: "verify", filesTarget: "files-dev", filesUrl: URL_, import: "imp-7" },
    ]);
  });
});

test("cancel posts to the job's cancel address with the control action", async () => {
  await withFetch(() => ({ id: "33333333-3333-4333-8333-333333333333", status: "cancelling" }), async (calls) => {
    const result = await deploymentsApi.cancelControl("ocharo-dev", "33333333-3333-4333-8333-333333333333");
    assert.equal(result.ok, true);
    assert.deepEqual(calls.map((call) => [call.url, call.method, call.action, call.body]),
      [["/api/deployments/targets/ocharo-dev/controls/33333333-3333-4333-8333-333333333333/cancel", "POST", "control", null]]);
  });
});
