import assert from "node:assert/strict";
import { after, test } from "node:test";
import { stop } from "esbuild";
import { bundleModule } from "./BundleModule.mjs";
import { container, target } from "./LoadModule.mjs";

// The real control rules, the pill and health rules a stopped environment reads through, and the wire schemas.
const {
  ControlOperation, ServiceControls, EnvironmentPillRules, PillState, EnvironmentHealth, EnvironmentHealthRules,
  AttentionRules, describeServiceObservation, TargetStateSchema, DeploymentItemSchema, PortfolioSummarySchema,
  ControlSubmittedSchema, DeploymentJobSchema, CommandLine, deploymentsApi,
} = await bundleModule(`
  export { CommandLine, ControlOperation, ServiceControls } from './src/domain/deployments/controls';
  export { deploymentsApi } from './src/integration/deployments';
  export { EnvironmentPillRules, PillState } from './src/domain/portfolio/EnvironmentPill';
  export { EnvironmentHealth, EnvironmentHealthRules } from './src/domain/overview/EnvironmentHealth';
  export { AttentionRules } from './src/domain/overview/AttentionRules';
  export { describeServiceObservation } from './src/presentation/product/serviceMap/ServiceObservation';
  export { ControlSubmittedSchema, DeploymentJobSchema, TargetStateSchema } from './src/integration/deployments/schemas';
  export { DeploymentItemSchema } from './src/integration/deployments/historySchemas';
  export { PortfolioSummarySchema } from './src/integration/portfolio/schemas';
`);
after(() => stop());

const dev = { id: "foreverpin-dev", environment: "dev", product: "foreverpin" };
const prod = { id: "foreverpin-prod", environment: "prod", product: "foreverpin" };

test("a running service stops or restarts; a stopped one starts; a stopped environment starts whole", () => {
  assert.deepEqual(ServiceControls.forService(null, "api"), [ControlOperation.Restart, ControlOperation.Stop]);
  assert.deepEqual(ServiceControls.forService({ environment: false, services: ["api"] }, "api"), [ControlOperation.Start]);
  assert.deepEqual(ServiceControls.forService({ environment: true, services: [] }, "api"), []);
  assert.equal(ServiceControls.forEnvironment(null), ControlOperation.Stop);
  assert.equal(ServiceControls.forEnvironment({ environment: false, services: ["api"] }), ControlOperation.Start);
});

test("Wheelhouse never offers controls on itself, and only prod asks for a typed ID and a reason", () => {
  assert.equal(ServiceControls.offers("wheelhouse", "wheelhouse"), false);
  assert.equal(ServiceControls.offers("foreverpin", "wheelhouse"), true);
  assert.equal(ServiceControls.isGuarded(prod), true);
  assert.equal(ServiceControls.isGuarded(dev), false);
});

test("a control reads by what it does", () => {
  assert.equal(ServiceControls.label(ControlOperation.Stop, "management"), "Stop management");
  assert.equal(ServiceControls.labelOf({ kind: "start", services: [] }), "Start the environment");
  assert.equal(ServiceControls.stateLabel({ environment: false, services: ["management"] }), "management stopped");
  assert.match(ServiceControls.consequence({ target: dev, operation: "stop", service: null }), /volumes and routes stay/);
});

test("a database reset takes the whole environment and says what it drops and what rebuilds it", () => {
  assert.equal(ControlOperation.Reset, "reset");
  assert.equal(ServiceControls.label(ControlOperation.Reset, null), "Reset the database");
  assert.equal(ServiceControls.labelOf({ kind: "reset", services: [] }), "Reset the database");
  assert.equal(ServiceControls.verb(ControlOperation.Reset), "Reset");
  const consequence = ServiceControls.consequence({ target: prod, operation: ControlOperation.Reset, service: null });
  assert.match(consequence, /^foreverpin-prod's database is snapshotted, then dropped and created again, empty/);
  assert.match(consequence, /next deploy's migrate step builds the schema/);
  assert.equal(ServiceControls.isGuarded(prod), true, "prod's reset asks for its target ID typed and a reason");
  const item = DeploymentItemSchema.parse({ id: "r1", status: "succeeded", outcome: "succeeded", state: "succeeded",
    attention: "none", submittedAt: "2026-10-29T10:00:00Z", operation: { kind: "reset", services: [] } });
  assert.deepEqual(item.operation, { kind: "reset", services: [] });
  const queued = ControlSubmittedSchema.parse({ id: "r1", targetId: "foreverpin-dev", status: "queued",
    operation: { kind: "reset", services: [] }, release: "v1.0.0" });
  assert.equal(queued.operation.kind, "reset");
});

test("a set reset names its set, rolls only it back and says which service stops", () => {
  const request = { target: prod, operation: ControlOperation.Reset, service: null,
    set: { name: "identity", service: "identity-api" } };
  assert.equal(ServiceControls.labelOfRequest(request), "Reset the identity migrations");
  assert.equal(ServiceControls.labelOfRequest({ ...request, set: null }), "Reset the database");
  assert.equal(ServiceControls.labelOf({ kind: "reset", services: [], set: "identity" }), "Reset the identity migrations");
  const consequence = ServiceControls.consequence(request);
  assert.match(consequence, /^The identity migration set on foreverpin-prod is rolled back to level 0 by the release it runs/);
  assert.match(consequence, /identity-api stops until the next deploy's migrate step builds the set again/);
  assert.match(consequence, /rest of the database keeps its data/);
  assert.doesNotMatch(consequence, /dropped/);
  const queued = ControlSubmittedSchema.parse({ id: "r2", targetId: "foreverpin-dev", status: "queued",
    operation: { kind: "reset", services: [], set: "identity" }, release: "v1.0.0" });
  assert.equal(queued.operation.set, "identity");
  const item = DeploymentItemSchema.parse({ id: "r2", status: "succeeded", outcome: "succeeded", state: "succeeded",
    attention: "none", submittedAt: "2026-11-01T10:00:00Z", operation: { kind: "reset", services: [], set: "identity" } });
  assert.equal(ServiceControls.labelOf(item.operation), "Reset the identity migrations");
  assert.equal(ControlSubmittedSchema.parse({ ...queued, operation: { kind: "reset", services: [], set: null } }).operation.set,
    null, "a whole-database reset names no set");
});

const harvest = { id: "harvest-dev", environment: "dev", product: "harvest" };

test("a copy names its source, says what it replaces and that the source stops first", () => {
  assert.equal(ControlOperation.Copy, "copy");
  assert.equal(ServiceControls.verb(ControlOperation.Copy), "Copy");
  assert.equal(ServiceControls.labelOf({ kind: "copy", services: [], from: "harvestadapters-dev" }),
    "Copy the database from harvestadapters-dev");
  assert.equal(ServiceControls.labelOf({ kind: "copy", services: [] }), "Copy the database from another environment");
  const request = { target: harvest, operation: ControlOperation.Copy, service: null, from: "harvestadapters-dev" };
  assert.equal(ServiceControls.labelOfRequest(request), "Copy the database from harvestadapters-dev");
  assert.equal(ServiceControls.titleOf(request), "Copy the database from harvestadapters-dev into harvest-dev?");
  assert.equal(ServiceControls.titleOf({ ...request, from: null }), "Copy the database from another environment into harvest-dev?");
  const consequence = ServiceControls.consequence(request);
  assert.match(consequence, /^harvest-dev's database is snapshotted, then replaced with a copy of harvestadapters-dev's database/);
  assert.match(consequence, /Stop harvestadapters-dev first, so nothing writes while it is read/);
  assert.match(consequence, /next deploy's migrate step reads the copied schema and starts them/);
  assert.equal(ServiceControls.isGuarded({ ...harvest, environment: "prod" }), true, "prod's copy asks for its target ID and a reason");
});

test("a copy reads only another environment on the same server, never Wheelhouse's own or its own twin", () => {
  const on = (id, product, environment, serverId, self = false) => ({ id, product, environment, serverId, self });
  const targets = [
    on("harvest-dev", "harvest", "dev", "products"), on("harvestadapters-dev", "harvestadapters", "dev", "products"),
    on("foreverpin-dev", "foreverpin", "dev", "products"), on("harvest-dev-2", "harvest", "dev", "products"),
    on("wheelhouse-dev", "wheelhouse", "dev", "products", true), on("harvestadapters-prod", "harvestadapters", "prod", "prod-host"),
  ];
  const sources = ServiceControls.copySources(harvest, targets);
  assert.deepEqual(sources.map((source) => source.id), ["foreverpin-dev", "harvestadapters-dev"]);
  assert.equal(ServiceControls.sourceLabel(sources[1]), "harvestadapters · dev");
  assert.deepEqual(ServiceControls.copySources({ ...harvest, id: "nowhere-dev" }, targets), [], "an unknown target has no sources");
});

test("the wire carries a copy's source and what it copied, and tolerates a copy it cannot read", () => {
  const operation = { kind: "copy", services: [], from: "harvestadapters-dev" };
  const copy = { from: "harvestadapters-dev", source: "harvestadapters_dev", database: "harvest_dev", tables: 12, rows: 48210 };
  const item = DeploymentItemSchema.parse({ id: "c7", status: "succeeded", outcome: "succeeded", state: "succeeded",
    attention: "none", submittedAt: "2026-11-12T10:00:00Z", operation, copy });
  assert.equal(ServiceControls.labelOf(item.operation), "Copy the database from harvestadapters-dev");
  assert.deepEqual(item.copy, copy);
  assert.equal(ServiceControls.copyResult(item.copy), "Copied 12 tables, 48,210 rows from harvestadapters_dev into harvest_dev");
  assert.equal(ServiceControls.copyResult({ ...copy, tables: null }), null, "no line before the counts were compared");
  assert.equal(ServiceControls.copyResult({ ...copy, tables: 1, rows: 1 }), "Copied 1 table, 1 row from harvestadapters_dev into harvest_dev");
  assert.equal(DeploymentItemSchema.parse({ id: "c8", status: "failed", outcome: "failed", state: "failed", attention: "act",
    submittedAt: "2026-11-12T10:00:00Z", operation, copy: "unreadable" }).copy, null, "an unreadable copy never fails the read");
  assert.deepEqual(DeploymentItemSchema.parse({ id: "c9", status: "succeeded", outcome: "succeeded", state: "succeeded",
    attention: "none", submittedAt: "2026-11-12T10:00:00Z", operation, copy: { ...copy, rows: -1, tables: "12" } }).copy,
    { ...copy, rows: null, tables: null }, "a count that is no whole number reads as none");
  const queued = ControlSubmittedSchema.parse({ id: "c7", targetId: "harvest-dev", status: "queued", operation, release: "v1.0.0" });
  assert.equal(queued.operation.from, "harvestadapters-dev");
  const job = DeploymentJobSchema.parse({ id: "c7", status: "succeeded", operation, copy });
  assert.deepEqual(job.copy, copy);
  assert.equal(DeploymentJobSchema.parse({ id: "d1", status: "succeeded" }).copy, undefined, "a deploy has no copy");
});

test("a copy sends its source, and no other control does", async () => {
  const bodies = [];
  const original = globalThis.fetch;
  globalThis.fetch = async (url, options) => {
    bodies.push([url, options.headers.get("X-Wheelhouse-Action"), JSON.parse(options.body)]);
    return new Response(JSON.stringify({ data: { id: "c7", targetId: "harvest-prod", status: "queued",
      operation: { kind: "copy", services: [], from: "harvestadapters-prod" } } }), { status: 200, headers: { "Content-Type": "application/json" } });
  };
  try {
    const copied = await deploymentsApi.control("harvest-prod", { operation: "copy", from: "harvestadapters-prod",
      reason: " move SCFD data ", confirm: "harvest-prod" });
    assert.equal(copied.ok, true);
    assert.equal(copied.value.operation.from, "harvestadapters-prod");
    await deploymentsApi.control("harvest-dev", { operation: "reset", from: "harvestadapters-dev" });
    assert.deepEqual(bodies.map(([url, action]) => [url, action]), [
      ["/api/deployments/targets/harvest-prod/controls", "control"],
      ["/api/deployments/targets/harvest-dev/controls", "control"],
    ]);
    assert.deepEqual(bodies[0][2], { operation: "copy", from: "harvestadapters-prod", reason: "move SCFD data", confirm: "harvest-prod" });
    assert.deepEqual(bodies[1][2], { operation: "reset" }, "only a copy names a source");
  } finally {
    globalThis.fetch = original;
  }
});

test("a stopped environment's pill reads stopped, never failed", () => {
  const summary = { environment: "dev", lastDeploy: null, running: null, weekly: [], lastAttempt: null,
    stopped: { environment: true, services: [] } };
  const pill = EnvironmentPillRules.of({ environment: "dev", reading: { environment: "dev", health: EnvironmentHealth.Failing,
    targetId: "foreverpin-dev" }, summary });
  assert.equal(pill.state, PillState.Stopped);
  const some = EnvironmentPillRules.of({ environment: "dev", reading: { environment: "dev", health: EnvironmentHealth.Healthy,
    targetId: "foreverpin-dev" }, summary: { ...summary, stopped: { environment: false, services: ["management"] } } });
  assert.deepEqual([some.state, some.detail], [PillState.Stopped, "1 stopped"]);
});

test("what the owner stopped is neither failing nor an attention item", () => {
  const exited = container({ service: "management", state: "exited", health: null, exitCode: 0 });
  const vitals = (stopped) => ({ collectedAt: "2026-10-26T10:00:00Z", servers: [], targets: [{ targetId: "foreverpin-rehearsal",
    serverId: "rehearsal", ok: true, containers: [exited], condition: "ready", release: "v1", stopped }] });
  assert.equal(EnvironmentHealthRules.ofTarget(target(), { vitals: vitals(null) }), EnvironmentHealth.Failing);
  assert.equal(EnvironmentHealthRules.ofTarget(target(), { vitals: vitals({ environment: false, services: ["management"] }) }),
    EnvironmentHealth.Healthy);
  assert.equal(EnvironmentHealthRules.ofTarget(target(), { vitals: vitals({ environment: true, services: [] }) }),
    EnvironmentHealth.Stopped);
  assert.equal(AttentionRules.fromVitals(vitals({ environment: false, services: ["management"] }))
    .some((item) => item.id.startsWith("container:")), false);
  assert.equal(describeServiceObservation("management", [exited], { environment: true, services: [] }).label, "Stopped");
});

test("the wire carries what is stopped and which items are controls", () => {
  const state = TargetStateSchema.parse({ targetId: "foreverpin-dev", project: "foreverpin-dev", condition: "ready",
    current: null, active: { id: "c1", status: "succeeded", operation: { kind: "stop", services: ["management"] } },
    stopped: { environment: false, services: ["management"], at: "2026-10-26T10:00:00Z", actor: "max" } });
  assert.deepEqual(state.stopped.services, ["management"]);
  assert.equal(state.active.operation.kind, "stop");
  const item = DeploymentItemSchema.parse({ id: "c1", status: "succeeded", outcome: "succeeded", state: "succeeded",
    attention: "none", submittedAt: "2026-10-26T10:00:00Z", operation: { kind: "restart", services: ["api"] } });
  assert.deepEqual(item.operation, { kind: "restart", services: ["api"] });
  assert.equal(DeploymentItemSchema.parse({ id: "d1", status: "succeeded", outcome: "succeeded", state: "succeeded",
    attention: "none", submittedAt: "2026-10-26T10:00:00Z" }).operation, null);
  const summary = PortfolioSummarySchema.parse({ timeZone: "UTC", weeks: [], workspaces: [], products: [{ product: "foreverpin",
    workspace: null, succeeded: 0, failed: 0, environments: [{ environment: "dev", lastDeploy: null, running: null, weekly: [],
      lastAttempt: null, stopped: { environment: true, services: [] } }] }] });
  assert.equal(summary.products[0].environments[0].stopped.environment, true);
});

test("a queued control reads back as the job its dialog follows to the end", () => {
  const queued = ControlSubmittedSchema.parse({ id: "c1", targetId: "wheelhousecanary-dev", status: "queued",
    operation: { kind: "restart", services: ["canary"] }, release: "v1.0.0" });
  assert.deepEqual([queued.id, queued.status, queued.operation.services], ["c1", "queued", ["canary"]]);
});

const identity = { id: "ocharo-dev", environment: "dev", product: "ocharo" };
const masked = "dotnet Identity.Api.dll admin create --slug … --name …";

test("a run reads by the words of its masked command, never by its arguments", () => {
  assert.equal(ControlOperation.Run, "run");
  assert.equal(ServiceControls.verb(ControlOperation.Run), "Run");
  assert.equal(ServiceControls.commandWords(masked), "admin create");
  assert.equal(ServiceControls.commandWords("python manage.py migrate"), "python manage.py migrate");
  assert.equal(ServiceControls.commandWords("sh -c …"), "sh");
  assert.equal(ServiceControls.commandWords("dotnet Identity.Api.dll --version"), "a command");
  assert.equal(ServiceControls.commandWords("--slug …"), "a command");
  assert.equal(ServiceControls.commandWords(null), "a command");
  assert.equal(ServiceControls.commandWords(""), "a command");
  assert.equal(ServiceControls.label(ControlOperation.Run, "identity", null, masked), "Run admin create on identity");
  assert.equal(ServiceControls.labelOf({ kind: "run", services: ["identity"], command: masked }), "Run admin create on identity");
  assert.equal(ServiceControls.labelOf({ kind: "run", services: ["identity"] }), "Run a command on identity");
  assert.equal(ServiceControls.labelOf({ kind: "run", services: ["identity"], command: null }), "Run a command on identity");
  // The confirmation has only the typed arguments, which never name it.
  const request = { target: identity, operation: ControlOperation.Run, service: "identity",
    command: ["dotnet", "Identity.Api.dll", "admin", "create", "--slug", "secret-slug"] };
  assert.equal(ServiceControls.labelOfRequest(request), "Run a command on identity");
  assert.equal(ServiceControls.titleOf(request), "Run a command in identity on ocharo-dev?");
  assert.equal(ServiceControls.titleOf({ target: dev, operation: ControlOperation.Stop, service: "management" }),
    "Stop management on foreverpin-dev?");
});

test("a run says where it runs, what stays running and what the job keeps", () => {
  const consequence = ServiceControls.consequence({ target: identity, operation: ControlOperation.Run, service: "identity" });
  assert.match(consequence, /^The command runs once in a one-off container of identity's running release/);
  assert.match(consequence, /settings and database network on ocharo-dev/);
  assert.match(consequence, /services keep running and nothing stopped changes/);
  assert.match(consequence, /exit code and the last output line stay on the job; the arguments do not/);
  assert.equal(ServiceControls.isGuarded({ ...identity, environment: "prod" }), true, "prod's run asks for its target ID and a reason");
  assert.deepEqual(ServiceControls.forService({ environment: false, services: ["identity"] }, "identity"), [ControlOperation.Start],
    "the service menu adds the run beside these, never inside them");
});

test("a finished run reads in one line", () => {
  const run = { service: "identity", command: masked, status: "succeeded", exitCode: 0, seconds: 3.4, lastLine: "Created ocharo" };
  assert.equal(ServiceControls.runResult(run), "Exited with code 0 in 3s");
  assert.equal(ServiceControls.runResult({ ...run, status: "failed", exitCode: 2, seconds: 125 }), "Exited with code 2 in 2m 5s");
  assert.equal(ServiceControls.runResult({ ...run, status: "timed out", exitCode: null, seconds: 120 }), "Timed out after 2m");
  assert.equal(ServiceControls.runResult({ ...run, status: "failed", exitCode: null, seconds: 1 }), "Ended without an exit code in 1s");
});

test("a typed command line splits into arguments the way a shell would, without expanding anything", () => {
  const args = (text) => {
    const split = CommandLine.split(text);
    assert.equal(split.ok, true, text);
    return split.args;
  };
  const error = (text) => {
    const split = CommandLine.split(text);
    assert.equal(split.ok, false, text);
    return split.error;
  };
  assert.deepEqual(args("dotnet Identity.Api.dll admin create --slug ocharo"),
    ["dotnet", "Identity.Api.dll", "admin", "create", "--slug", "ocharo"]);
  assert.deepEqual(args("  a \t b\t\tc  "), ["a", "b", "c"], "any whitespace separates, and the ends are free");
  assert.deepEqual(args(`create --name "Ocharo Studio" --note 'it is "fine"'`),
    ["create", "--name", "Ocharo Studio", "--note", 'it is "fine"']);
  assert.deepEqual(args(`--name="Ocharo Studio"`), ["--name=Ocharo Studio"], "quoted and bare parts join into one argument");
  assert.deepEqual(args(`a"b c"d 'e f'g`), ["ab cd", "e fg"]);
  assert.deepEqual(args("echo $HOME ~ * ${X} `id` \\n"), ["echo", "$HOME", "~", "*", "${X}", "`id`", "\\n"],
    "no variables, globs or escapes: a backslash is an ordinary character");
  assert.deepEqual(args("'  ' x"), ["  ", "x"], "a quoted space is an argument of its own");
  assert.deepEqual(args(""), []);
  assert.deepEqual(args("   "), []);
  assert.match(error(`create --name "Ocharo`), /The " quote is never closed/);
  assert.match(error("create --name 'Ocharo"), /The ' quote is never closed/);
  assert.match(error(`a "" b`), /cannot be empty/);
  assert.match(error('a "x\ny"'), /line break/);
  assert.deepEqual(args("a\nb"), ["a", "b"], "a line break outside quotes only separates");
  assert.equal(args(Array.from({ length: 40 }, (_, index) => `a${index}`).join(" ")).length, 40);
  assert.match(error(Array.from({ length: 41 }, (_, index) => `a${index}`).join(" ")), /at most 40 arguments; this has 41/);
  assert.equal(args("x".repeat(500)).length, 1);
  assert.match(error("x".repeat(501)), /longer than 500 characters/);
});

test("the wire carries a run's command label and how it ended, and tolerates a run it cannot read", () => {
  const queued = ControlSubmittedSchema.parse({ id: "r9", targetId: "ocharo-dev", status: "queued",
    operation: { kind: "run", services: ["identity"], command: masked }, release: "v1.0.0" });
  assert.deepEqual([queued.operation.kind, queued.operation.command], ["run", masked]);
  assert.equal(ControlSubmittedSchema.parse({ ...queued, operation: { kind: "stop", services: [] } }).operation.command, undefined);
  const item = DeploymentItemSchema.parse({ id: "r9", status: "succeeded", outcome: "succeeded", state: "succeeded",
    attention: "none", submittedAt: "2026-11-02T10:00:00Z", operation: { kind: "run", services: ["identity"], command: masked } });
  assert.equal(ServiceControls.labelOf(item.operation), "Run admin create on identity");
  const run = { service: "identity", command: masked, status: "timed out", exitCode: null, seconds: 120.5, lastLine: null };
  const job = DeploymentJobSchema.parse({ id: "r9", status: "failed", operation: queued.operation, run });
  assert.deepEqual(job.run, run);
  assert.equal(DeploymentJobSchema.parse({ id: "d1", status: "succeeded" }).run, undefined, "a deploy has no run");
  assert.equal(DeploymentJobSchema.parse({ id: "r9", status: "succeeded", run: { status: "exploded" } }).run, null,
    "a run this console cannot read never fails the outcome read");
  assert.equal(DeploymentJobSchema.parse({ id: "r9", status: "succeeded", run: null }).run, null);
});

test("a run sends its split command, and no other control does", async () => {
  const bodies = [];
  const original = globalThis.fetch;
  globalThis.fetch = async (url, options) => {
    bodies.push([url, options.headers.get("X-Wheelhouse-Action"), JSON.parse(options.body)]);
    return new Response(JSON.stringify({ data: { id: "r9", targetId: "ocharo-prod", status: "queued",
      operation: { kind: "run", services: ["identity"], command: masked } } }), { status: 200, headers: { "Content-Type": "application/json" } });
  };
  try {
    const command = ["dotnet", "Identity.Api.dll", "admin", "create", "--name", "Ocharo Studio"];
    const run = await deploymentsApi.control("ocharo-prod", { operation: "run", service: "identity", command,
      reason: " first admin ", confirm: "ocharo-prod" });
    assert.equal(run.ok, true);
    await deploymentsApi.control("ocharo-dev", { operation: "restart", service: "identity", command: [] });
    await deploymentsApi.control("ocharo-dev", { operation: "stop", service: "identity", command: null });
    assert.deepEqual(bodies.map(([url, action]) => [url, action]), [
      ["/api/deployments/targets/ocharo-prod/controls", "control"],
      ["/api/deployments/targets/ocharo-dev/controls", "control"],
      ["/api/deployments/targets/ocharo-dev/controls", "control"],
    ]);
    assert.deepEqual(bodies[0][2], { operation: "run", service: "identity", command, reason: "first admin", confirm: "ocharo-prod" });
    assert.deepEqual(bodies[1][2], { operation: "restart", service: "identity" });
    assert.deepEqual(bodies[2][2], { operation: "stop", service: "identity" });
  } finally {
    globalThis.fetch = original;
  }
});

test("a run whose line was cut offers one read, which posts to its job and parses the line", async () => {
  const run = { service: "identity", command: masked, status: "succeeded", exitCode: 0, seconds: 2,
    lastLine: "admin-result {\"link\":\"https://id.test/sign-up…\"}", reveal: { expiresAt: "2026-11-02T15:15:00Z" } };
  assert.deepEqual(DeploymentJobSchema.parse({ id: "r9", status: "succeeded", run }).run.reveal, run.reveal);
  assert.equal(DeploymentJobSchema.parse({ id: "r9", status: "succeeded", run: { ...run, reveal: 3 } }).run.reveal, null,
    "an unreadable reveal never fails the run");
  const calls = [];
  const original = globalThis.fetch;
  globalThis.fetch = async (url, options) => {
    calls.push([url, options.method, options.headers.get("X-Wheelhouse-Action")]);
    return new Response(JSON.stringify({ data: { line: "admin-result {\"link\":\"https://id.test/sign-up?invitation=T\"}" } }),
      { status: 200, headers: { "Content-Type": "application/json" } });
  };
  try {
    const read = await deploymentsApi.revealRun("ocharo-dev", "33333333-3333-4333-8333-333333333333");
    assert.equal(read.ok, true);
    assert.match(read.value.line, /invitation=T/);
    assert.deepEqual(calls, [["/api/deployments/targets/ocharo-dev/controls/33333333-3333-4333-8333-333333333333/reveal", "POST", "control"]]);
  } finally {
    globalThis.fetch = original;
  }
});

test("a run's stdin file is base64 of 1 byte to 2 MiB, and a run keeps its tail and the file's size and hash", async () => {
  const { RunFiles } = await bundleModule(`export { RunFiles } from './src/domain/deployments/controls';`);
  assert.equal(RunFiles.problem(0), "The file is empty.");
  assert.equal(RunFiles.problem(RunFiles.limit), null);
  assert.match(RunFiles.problem(RunFiles.limit + 1), /2 MiB at most/);
  const bytes = new Uint8Array(70000).map((_, index) => index % 251);
  assert.equal(RunFiles.toBase64(bytes), Buffer.from(bytes).toString("base64"));
  assert.equal(RunFiles.label({ bytes: 231536, sha256: "308deb17c5f5ae88aaaa" }), "Took a 231536-byte file on its stdin · SHA-256 308deb17c5f5ae88…");
  assert.equal(RunFiles.label(null), null);
  const job = DeploymentJobSchema.parse({
    id: "11111111-1111-4111-8111-111111111111", status: "succeeded", actor: "max",
    run: { service: "events", command: "dotnet Events.Api.dll import …", status: "succeeded", exitCode: 0, seconds: 3,
      lastLine: "import-result ok=true", tail: ["import-report {\"map\":[]}"], input: { bytes: 4, sha256: "ab" } },
  });
  assert.deepEqual(job.run.tail, ["import-report {\"map\":[]}"]);
  assert.deepEqual(job.run.input, { bytes: 4, sha256: "ab" });
});
