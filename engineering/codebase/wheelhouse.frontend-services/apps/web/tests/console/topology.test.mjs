import assert from "node:assert/strict";
import { after, afterEach, test } from "node:test";
import { readFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { build, stop } from "esbuild";
import { compileScript, parse } from "@vue/compiler-sfc";

const bundle = await build({
  stdin: {
    contents: `
      export { buildServiceMapLayout } from './src/presentation/product/serviceMap/ServiceMapLayout';
      export { describeServiceObservation } from './src/presentation/product/serviceMap/ServiceObservation';
      export { describeServiceVersion } from './src/presentation/product/serviceMap/ServiceVersion';
      export { ServiceTopologySchema } from './src/integration/topology/schemas';
      export { topologyApi } from './src/integration/topology';
      export { useTargetTopology, TopologyKeys } from './src/application/topology';
      export { queryClient, queryPlugin } from './src/bootstrap/query';
      export { default as ServiceMap } from './src/presentation/product/ServiceMap.vue';
      export { createRenderer, nextTick, ref } from 'vue';
    `,
    resolveDir: process.cwd(),
  },
  alias: { "@": resolve("src") },
  bundle: true,
  platform: "node",
  format: "esm",
  write: false,
  plugins: [
    {
      name: "vue-setup-lifecycle",
      setup(plugin) {
        plugin.onLoad({ filter: /\.vue$/ }, async ({ path }) => {
          const source = await readFile(path, "utf8");
          const script = compileScript(
            parse(source, { filename: path }).descriptor,
            { id: path },
          );
          return {
            contents: script.content,
            loader: "ts",
            resolveDir: dirname(path),
          };
        });
      },
    },
  ],
});
const {
  buildServiceMapLayout,
  describeServiceObservation,
  describeServiceVersion,
  ServiceTopologySchema,
  topologyApi,
  useTargetTopology,
  TopologyKeys,
  queryClient,
  queryPlugin,
  ServiceMap,
  createRenderer,
  nextTick,
  ref,
} = await import(
  `data:text/javascript;base64,${Buffer.from(bundle.outputFiles[0].text).toString("base64")}`
);
const originalFetch = globalThis.fetch;
afterEach(() => {
  globalThis.fetch = originalFetch;
  queryClient.clear();
});
after(() => stop());

function topology(overrides = {}) {
  return {
    targetId: "product-staging",
    availability: "available",
    release: "1.2.3",
    collectedAt: "2026-09-26T12:00:00Z",
    services: [
      {
        name: "api",
        image: "ghcr.io/example/api:1.2.3",
        networks: ["public", "internal"],
        volumes: [],
        ports: ["8080:80/tcp"],
      },
      {
        name: "db",
        image: "postgres:17",
        networks: ["internal"],
        volumes: ["db-data"],
        ports: [],
      },
      {
        name: "worker",
        image: null,
        networks: ["jobs"],
        volumes: [],
        ports: [],
      },
    ],
    networks: [
      { name: "public", external: true },
      { name: "internal", external: false },
      { name: "jobs", external: false },
    ],
    volumes: [{ name: "db-data", external: false }],
    dependencies: [
      { from: "api", to: "db", condition: "service_healthy", required: true },
    ],
    warnings: [],
    ...overrides,
  };
}
function json(value) {
  return new Response(JSON.stringify(value), {
    headers: { "Content-Type": "application/json" },
  });
}
function container(overrides = {}) {
  return {
    service: "api",
    state: "running",
    health: "healthy",
    restarts: 0,
    startedAt: null,
    exitCode: null,
    cpuPercent: null,
    memoryBytes: null,
    memoryLimitBytes: null,
    ...overrides,
  };
}
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
function mountMap(extra = {}) {
  const events = [];
  const app = renderer.createApp(
    { ...ServiceMap, render: () => null },
    {
      topology: topology(),
      onSelect: (name) => events.push(name),
      ...extra,
    },
  );
  app.mount({});
  return {
    app,
    events,
    state: app._instance.setupState,
    props: app._instance.props,
  };
}

test("maps shared networks through one hub without inventing service-to-service traffic", () => {
  const input = topology({ dependencies: [] });
  const result = buildServiceMapLayout(input);
  assert.equal(
    result.nodes.filter((node) => node.kind === "network").length,
    3,
  );
  assert.equal(result.edges.length, 4);
  assert.ok(result.edges.every((edge) => edge.kind === "network"));
  assert.deepEqual(
    result.edges
      .filter((edge) => edge.from === "network:internal")
      .map((edge) => edge.to),
    ["service:api", "service:db"],
  );
  assert.deepEqual(
    result.edges
      .filter((edge) => edge.from === "network:jobs")
      .map((edge) => edge.to),
    ["service:worker"],
  );
});

test("retains deterministic startup arrows for cycles, self-dependencies, and shuffled declarations", () => {
  const input = topology({
    dependencies: [
      { from: "db", to: "api", condition: null, required: true },
      { from: "api", to: "db", condition: "service_healthy", required: true },
      { from: "worker", to: "worker", condition: null, required: false },
    ],
  });
  const original = structuredClone(input);
  const result = buildServiceMapLayout(input, true);
  const shuffled = {
    ...input,
    services: [...input.services]
      .reverse()
      .map((service) => ({
        ...service,
        networks: [...service.networks].reverse(),
      })),
    networks: [...input.networks].reverse(),
    volumes: [...input.volumes].reverse(),
    dependencies: [...input.dependencies].reverse(),
  };
  assert.deepEqual(buildServiceMapLayout(shuffled, true), result);
  assert.deepEqual(
    input,
    original,
    "layout must not mutate the retained query payload",
  );
  assert.equal(
    result.edges.filter((edge) => edge.kind === "dependency").length,
    3,
  );
  assert.ok(
    result.edges.every((edge) => !/NaN|undefined|Infinity/.test(edge.path)),
  );
  assert.equal(
    result.edges.find(
      (edge) => edge.kind === "dependency" && edge.from === "service:api",
    ).to,
    "service:db",
  );
});

test("named volumes remain optional while disconnected services remain present", () => {
  const input = topology();
  input.services.push({
    name: "isolated",
    image: null,
    networks: [],
    volumes: [],
    ports: [],
  });
  const compact = buildServiceMapLayout(input);
  const expanded = buildServiceMapLayout(input, true);
  assert.equal(
    compact.nodes.filter((node) => node.kind === "volume").length,
    0,
  );
  assert.equal(
    expanded.nodes.filter((node) => node.kind === "volume").length,
    1,
  );
  assert.equal(
    expanded.edges.filter((edge) => edge.kind === "volume").length,
    1,
  );
  assert.ok(compact.nodes.some((node) => node.id === "service:isolated"));
  assert.ok(
    !compact.edges.some(
      (edge) =>
        edge.from === "service:isolated" || edge.to === "service:isolated",
    ),
  );
  for (const node of expanded.nodes) {
    assert.ok(
      node.x >= 0 &&
        node.y >= 0 &&
        node.x + node.width <= expanded.width &&
        node.y + node.height <= expanded.height,
    );
  }
});

test("unavailable and not-deployed declarations produce no nodes or successful empty-map claims", () => {
  for (const availability of ["unavailable", "not-deployed"]) {
    const input = topology({
      availability,
      release: null,
      services: [],
      networks: [],
      volumes: [],
      dependencies: [],
    });
    assert.equal(ServiceTopologySchema.safeParse(input).success, true);
    assert.deepEqual(buildServiceMapLayout(input).nodes, []);
    assert.deepEqual(buildServiceMapLayout(input).edges, []);
  }
});

test("schema strips non-projected fields and rejects unsafe identifiers, host bindings, and dangling references", () => {
  const input = topology();
  input.environment = { PASSWORD: "never-render-this" };
  input.services[0].environment = { SECRET: "never-render-this" };
  input.networks[0].resourceName = "${PLATFORM_NETWORK}";
  const parsed = ServiceTopologySchema.parse(input);
  assert.ok(!JSON.stringify(parsed).includes("never-render-this"));
  assert.ok(!JSON.stringify(parsed).includes("PLATFORM_NETWORK"));
  for (const mutate of [
    (value) => value.services.push(value.services[0]),
    (value) => value.networks.push(value.networks[0]),
    (value) => value.services[0].networks.push("missing"),
    (value) => value.services[0].volumes.push("/private/key"),
    (value) => value.services[0].ports.push("127.0.0.1:8080:80/tcp"),
    (value) => value.services[0].ports.push("${PRIVATE_PORT}:80/tcp"),
    (value) => (value.dependencies[0].to = "missing"),
    (value) => value.dependencies.push(value.dependencies[0]),
    (value) => (value.networks[0].name = "${PLATFORM_NETWORK}"),
    (value) => (value.availability = "unavailable"),
    (value) => (value.collectedAt = "not-a-time"),
  ]) {
    const invalid = topology();
    mutate(invalid);
    assert.equal(ServiceTopologySchema.safeParse(invalid).success, false);
  }
});

test("runtime overlay distinguishes unread snapshots, unobserved declarations, and observed health", () => {
  assert.equal(
    describeServiceObservation("api", null).label,
    "Runtime unavailable",
  );
  assert.equal(
    describeServiceObservation("api", []).label,
    "Declared · not observed",
  );
  assert.equal(
    describeServiceObservation("api", [container()]).tone,
    "success",
  );
  assert.equal(
    describeServiceObservation("api", [container({ health: "unhealthy" })])
      .tone,
    "danger",
  );
  assert.equal(
    describeServiceObservation("api", [container(), container()]).label,
    "2 containers observed",
  );
  assert.equal(describeServiceObservation("missing", [container()]).count, 0);
});

test("reads the encoded target through the management envelope and same-origin credentials", async () => {
  let captured;
  globalThis.fetch = async (url, options) => {
    captured = { url, options };
    return json({ data: topology({ targetId: "target/one" }) });
  };
  const result = await topologyApi.getTargetTopology("target/one");
  assert.equal(result.ok, true);
  assert.equal(captured.url, "/api/deployments/targets/target%2Fone/topology");
  assert.equal(captured.options.credentials, "same-origin");
  assert.equal(captured.options.headers.has("X-Wheelhouse-Action"), false);
});

test("rejects a different target response and an un-enveloped topology", async () => {
  globalThis.fetch = async () =>
    json({ data: topology({ targetId: "different-target" }) });
  const wrongTarget = await topologyApi.getTargetTopology("product-staging");
  assert.equal(wrongTarget.ok, false);
  assert.equal(wrongTarget.failure.code, "protocol");
  globalThis.fetch = async () => json(topology());
  assert.equal(
    (await topologyApi.getTargetTopology("product-staging")).ok,
    false,
  );
});

test("changing selected targets aborts stale topology reads and never reuses a different target map", async () => {
  const requests = [];
  globalThis.fetch = (url, options) =>
    new Promise((settle) =>
      requests.push({ url, signal: options.signal, settle }),
    );
  const selected = ref(null);
  let query;
  const app = renderer.createApp({
    setup() {
      query = useTargetTopology(selected);
      return () => null;
    },
  });
  app.use(queryPlugin(queryClient));
  app.mount({});
  try {
    await nextTick();
    assert.equal(requests.length, 0);
    selected.value = "first";
    await new Promise((resolve) => setImmediate(resolve));
    assert.equal(requests.length, 1);
    selected.value = "second";
    await new Promise((resolve) => setImmediate(resolve));
    assert.equal(requests[0].signal.aborted, true);
    requests[0].settle(json({ data: topology({ targetId: "first" }) }));
    requests[1].settle(json({ data: topology({ targetId: "second" }) }));
    await new Promise((resolve) => setImmediate(resolve));
    assert.equal(query.data.value.targetId, "second");
    assert.deepEqual(TopologyKeys.target("second"), [
      "deployments",
      "topology",
      "second",
    ]);
  } finally {
    app.unmount();
  }
});

test("declared-only service selection opens local details and follows external selection without emitting on mount", async () => {
  const mounted = mountMap({ containers: [] });
  try {
    assert.deepEqual(mounted.events, []);
    assert.equal(mounted.state.selected, null);
    mounted.state.choose("worker");
    assert.equal(mounted.state.selected.name, "worker");
    assert.equal(
      mounted.state.selectedObservation.label,
      "Declared · not observed",
    );
    assert.deepEqual(mounted.events, ["worker"]);
    mounted.props.selectedService = "api";
    await nextTick();
    assert.equal(mounted.state.selected.name, "api");
    assert.equal(mounted.state.dependencies[0].to, "db");
    mounted.props.selectedService = null;
    mounted.props.topology = topology({ targetId: "next-target" });
    await nextTick();
    assert.equal(mounted.state.selected, null);
    assert.deepEqual(mounted.events, ["worker"]);
  } finally {
    mounted.app.unmount();
  }
});

test("sites and platform services join the right column, one hub per need, and can be hidden", () => {
  const input = topology({
    services: [
      {
        name: "api",
        image: null,
        networks: ["internal"],
        volumes: [],
        ports: [],
        version: { version: "1.2.0", changedIn: "v1.2.0" },
        needs: ["postgres"],
        sites: [
          { name: "app", path: "/api", port: 8080, exposure: "public", url: "http://app.localhost/api", reachable: false },
        ],
      },
      {
        name: "worker",
        image: null,
        networks: ["internal"],
        volumes: [],
        ports: [],
        version: null,
        needs: ["postgres", "valkey"],
        sites: [],
      },
    ],
    networks: [{ name: "internal", external: false }],
    volumes: [],
    dependencies: [],
  });
  const result = buildServiceMapLayout(input);
  const site = result.nodes.find((node) => node.kind === "site");
  assert.equal(site.name, "app/api");
  assert.equal(site.href, "http://app.localhost/api");
  assert.equal(site.attention, true);
  assert.deepEqual(
    result.nodes.filter((node) => node.kind === "platform").map((node) => node.name),
    ["postgres", "valkey"],
  );
  assert.equal(result.edges.filter((edge) => edge.kind === "platform").length, 3);
  assert.equal(result.edges.filter((edge) => edge.kind === "site").length, 1);
  assert.equal(result.width, 752);
  const hidden = buildServiceMapLayout(input, false, { sites: false, platform: false });
  assert.equal(hidden.nodes.some((node) => node.kind === "site" || node.kind === "platform"), false);
  assert.equal(hidden.width, 584);
  for (const node of result.nodes)
    assert.ok(node.x + node.width <= result.width && node.y + node.height <= result.height);
});

test("schema defaults release facts for an older runner and refuses a non-http site address", () => {
  const older = ServiceTopologySchema.safeParse(topology());
  assert.equal(older.success, true);
  assert.deepEqual(
    [older.data.services[0].version, older.data.services[0].needs, older.data.services[0].sites],
    [null, [], []],
  );
  const unsafe = topology();
  unsafe.services[0].sites = [
    { name: "app", path: "/", port: 8080, exposure: "public", url: "javascript:alert(1)", reachable: null },
  ];
  assert.equal(ServiceTopologySchema.safeParse(unsafe).success, false);
});

test("every service shows a version: its own when recorded, else the release it runs", () => {
  assert.deepEqual(
    describeServiceVersion({ version: { version: "1.2.0", changedIn: "v1.2.0" } }, "v1.3.0"),
    { label: "1.2.0", detail: "Last changed in v1.2.0", isServiceVersion: true },
  );
  const fallback = describeServiceVersion({ version: null }, "rehearsal-1");
  assert.equal(fallback.label, "rehearsal-1");
  assert.equal(fallback.isServiceVersion, false);
  assert.equal(describeServiceVersion({ version: null }, null).label, "unversioned");
});
