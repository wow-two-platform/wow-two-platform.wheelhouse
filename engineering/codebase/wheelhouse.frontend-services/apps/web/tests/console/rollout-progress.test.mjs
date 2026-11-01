import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { resolve } from "node:path";
import { after, test } from "node:test";
import { build, stop } from "esbuild";
import { compileScript, compileTemplate, parse, rewriteDefault } from "@vue/compiler-sfc";
import { createSSRApp, h } from "vue";
import { renderToString } from "vue/server-renderer";

// The deploy dialog's follow view: its title by state, its services table and its step timeline. The real rules are
// bundled as is; the real components are compiled for server rendering, with packages resolved to the installed copies
// so they and this harness share one Vue.
const bundle = await build({
  stdin: {
    contents: `
      export { JobStatus, RolloutProgress, ServiceMark, StepStatus } from './src/domain/deployments';
      export { DeploymentJobSchema } from './src/integration/deployments/schemas';
      export { default as DeploymentSteps } from './src/presentation/deployments/components/DeploymentSteps.vue';
      export { default as RolloutServicesTable } from './src/presentation/deployments/components/RolloutServicesTable.vue';
    `,
    resolveDir: process.cwd(),
  },
  alias: { "@": resolve("src") },
  bundle: true,
  packages: "external",
  platform: "node",
  format: "esm",
  write: false,
  plugins: [
    {
      name: "sfc-ssr",
      setup(build) {
        build.onLoad({ filter: /\.vue$/ }, async (args) => {
          const { descriptor } = parse(await readFile(args.path, "utf8"), { filename: args.path });
          const script = compileScript(descriptor, { id: args.path });
          const template = compileTemplate({
            source: descriptor.template.content,
            filename: args.path,
            id: args.path,
            ssr: true,
            ssrCssVars: [],
            compilerOptions: { bindingMetadata: script.bindings },
          });
          const contents = `${rewriteDefault(script.content, "__sfc", ["typescript"])}\n${template.code}\n__sfc.ssrRender = ssrRender;\nexport default __sfc;`;
          return { contents, loader: "ts" };
        });
      },
    },
  ],
});
const code = bundle.outputFiles[0].text.replace(
  /(from\s+["'])([^"'./][^"']*)(["'])/g,
  (_match, before, name, end) => `${before}${import.meta.resolve(name)}${end}`,
);
const { JobStatus, RolloutProgress, ServiceMark, StepStatus, DeploymentJobSchema, DeploymentSteps, RolloutServicesTable } =
  await import(
  `data:text/javascript;base64,${Buffer.from(code).toString("base64")}`
);
after(() => stop());

const render = (component, props) => renderToString(createSSRApp({ render: () => h(component, props) }));

const service = (name, change, outcome, from, to, health = "unknown") => ({
  name,
  change,
  outcome,
  dependsOn: [],
  from,
  to,
  reason: change === "unchanged" ? null : "image",
  health,
});

/** Ocharo's prod rollout of 2026-11-01: one service of ten changed, the rest kept running. */
const ocharo = (outcome = "updated", health = "healthy") => ({
  staged: true,
  waves: [["studio"]],
  removed: [],
  services: [
    service("assets", "unchanged", "unchanged", "0.1.1", "0.1.1"),
    service("blog", "unchanged", "unchanged", "0.1.0", "0.1.0"),
    service("studio", "updated", outcome, "0.1.6", "0.1.7", health),
    service("marketing", "unchanged", "unchanged", "0.1.0", "0.1.0"),
  ],
});

test("the outcome read keeps the runner's service rollout, and a malformed one never fails the read", () => {
  const job = { id: "11111111-1111-4111-8111-111111111111", status: "running", targetId: "ocharo-prod" };
  const read = DeploymentJobSchema.parse({ ...job, serviceRollout: { ...ocharo("pending"), interpolation: "dropped" } });
  assert.deepEqual(
    read.serviceRollout.services.map((item) => [item.name, item.outcome, item.from, item.to]),
    [
      ["assets", "unchanged", "0.1.1", "0.1.1"],
      ["blog", "unchanged", "0.1.0", "0.1.0"],
      ["studio", "pending", "0.1.6", "0.1.7"],
      ["marketing", "unchanged", "0.1.0", "0.1.0"],
    ],
  );
  assert.equal(DeploymentJobSchema.parse({ ...job, serviceRollout: { services: "nope" } }).serviceRollout, null);
  assert.equal(DeploymentJobSchema.parse(job).serviceRollout, undefined);
});

test("the title names where the rollout stands, never the release", () => {
  assert.equal(RolloutProgress.title(undefined), "Deploying updated services");
  assert.equal(RolloutProgress.title(JobStatus.Queued), "Deploying updated services");
  assert.equal(RolloutProgress.title(JobStatus.Running, ocharo("pending")), "Deploying updated services");
  assert.equal(RolloutProgress.title(JobStatus.Succeeded, ocharo()), "Deployed updated services");
  assert.equal(RolloutProgress.title(JobStatus.Failed, ocharo("failed")), "Deploy failed");
  assert.equal(RolloutProgress.title(JobStatus.RolledBack, ocharo("rolled_back")), "Deploy rolled back");
  assert.equal(RolloutProgress.title(JobStatus.RollbackFailed), "Deploy and its roll back failed");
  assert.equal(RolloutProgress.title(JobStatus.Rejected), "Deploy refused");
  const same = { ...ocharo(), services: ocharo().services.filter((item) => item.change === "unchanged") };
  assert.equal(RolloutProgress.title(JobStatus.Succeeded, same), "No service needed an update");
});

test("the table puts what changed first, its old version stopped and its new one healthy; the rest kept running", () => {
  const rows = RolloutProgress.rows(ocharo(), JobStatus.Succeeded);
  assert.deepEqual(
    rows.map((row) => row.service),
    ["studio", "assets", "blog", "marketing"],
  );
  assert.deepEqual(rows[0], {
    service: "studio",
    changed: true,
    current: { version: "0.1.6", mark: ServiceMark.Stopped },
    next: { version: "0.1.7", mark: ServiceMark.Healthy },
  });
  assert.deepEqual(rows[1], {
    service: "assets",
    changed: false,
    current: { version: "0.1.1", mark: ServiceMark.Running },
    next: null,
  });
  assert.equal(RolloutProgress.summary(ocharo()), "1 of 4 services changed; 3 kept running");
});

test("a live rollout marks the services starting now, and the rest waiting with their old version still up", () => {
  const rollout = {
    staged: true,
    waves: [["api"], ["web"]],
    removed: [],
    services: [service("api", "updated", "pending", "1.0.0", "1.1.0"), service("web", "updated", "pending", "1.0.0", "1.1.0")],
  };
  const steps = [
    { name: "Pull images", status: StepStatus.Succeeded },
    { name: "Start api", status: StepStatus.Running },
  ];
  const rows = RolloutProgress.rows(rollout, JobStatus.Running, steps);
  assert.deepEqual(
    rows.map((row) => [row.service, row.current.mark, row.next?.mark]),
    [
      ["api", ServiceMark.Running, ServiceMark.Starting],
      ["web", ServiceMark.Running, ServiceMark.Waiting],
    ],
  );
  const converging = RolloutProgress.rows(rollout, JobStatus.Running, [{ name: "Start containers", status: StepStatus.Running }]);
  assert.deepEqual(converging.map((row) => row.next?.mark), [ServiceMark.Starting, ServiceMark.Starting]);
});

test("a failed rollout marks the failure, what never started, what rolled back, and a new or dropped service", () => {
  const rollout = {
    staged: true,
    waves: [["jobs"], ["desk", "mailer"]],
    removed: ["legacy"],
    services: [
      service("desk", "updated", "not_started", "0.8.0", "0.9.0"),
      service("jobs", "updated", "failed", "0.8.0", "0.9.0", "unhealthy"),
      service("mailer", "added", "rolled_back", null, "0.1.0"),
    ],
  };
  const rows = RolloutProgress.rows(rollout, JobStatus.RolledBack);
  assert.deepEqual(
    rows.map((row) => [row.service, row.current, row.next]),
    [
      ["jobs", { version: "0.8.0", mark: ServiceMark.Stopped }, { version: "0.9.0", mark: ServiceMark.Failed }],
      ["desk", { version: "0.8.0", mark: ServiceMark.Running }, { version: "0.9.0", mark: ServiceMark.NotStarted }],
      ["mailer", { version: null, mark: null }, { version: "0.1.0", mark: ServiceMark.RolledBack }],
      ["legacy", { version: null, mark: null }, { version: null, mark: ServiceMark.Removed }],
    ],
  );
  assert.deepEqual(RolloutProgress.marks(rows), [
    ServiceMark.Running,
    ServiceMark.Stopped,
    ServiceMark.Failed,
    ServiceMark.NotStarted,
    ServiceMark.RolledBack,
    ServiceMark.Removed,
  ]);
  assert.equal(RolloutProgress.summary(rollout), "3 services changed; removed legacy");
});

test("the services table is a real table, with each version's mark in words and a legend of the marks shown", async () => {
  const rows = RolloutProgress.rows(ocharo(), JobStatus.Succeeded);
  const html = await render(RolloutServicesTable, { rows, summary: RolloutProgress.summary(ocharo()) });
  assert.match(html, /<table[^>]*>\s*<caption/);
  assert.match(html, /<th scope="col"[^>]*>\s*Current\s*<\/th>/);
  assert.match(html, /<th scope="col"[^>]*>\s*Next\s*<\/th>/);
  assert.match(html, /<th scope="row"[^>]*>\s*studio\s*<\/th>/);
  assert.match(html, /0\.1\.6<\/span><span class="sr-only">, stopped<\/span>/);
  assert.match(html, /0\.1\.7<\/span><span class="sr-only">, started, healthy<\/span>/);
  assert.match(html, />unchanged</);
  assert.match(html, /1 of 4 services changed; 3 kept running/);
  assert.match(html, /aria-label="What the marks mean"/);
  assert.doesNotMatch(html, /Open |failed/);
});

test("a skipped step reads as skipped in words, with a dashed node, never a minus beside its kind's glyph", async () => {
  const steps = [
    { name: "Verify services", status: StepStatus.Succeeded, detail: "10 services healthy" },
    { name: "Remove unused images", status: StepStatus.Skipped, detail: "No image only older releases used" },
  ];
  const html = await render(DeploymentSteps, { steps });
  const skipped = html.slice(html.indexOf('data-status="skipped"'));
  assert.match(skipped, /lucide-circle-dashed/);
  assert.doesNotMatch(html, /lucide-circle-minus/);
  assert.match(skipped, />skipped<\/span>/);
  assert.match(skipped, /, skipped, nothing to do</);
  // One line joins the two nodes: the last step draws none.
  assert.equal(html.match(/bottom-0 w-px bg-border-strong/g)?.length, 1);
});
