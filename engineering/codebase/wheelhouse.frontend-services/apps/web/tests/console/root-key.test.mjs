import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { readFileSync } from "node:fs";
import { after, test } from "node:test";
import { stop } from "esbuild";
import { bundleModule } from "./BundleModule.mjs";

// The root key's checks in the console: the offline copy's line and the file check's finding in words, the owner's
// fingerprint command, and the wire decoding of the vault's status and issues, older consoles' included.
const loaded = await bundleModule(`
  export { RootKeyRules } from './src/domain/settings';
  export { VaultStatusSchema, VaultIssuesSchema } from './src/integration/secrets/vaultApi';
`);
after(() => stop());
const { RootKeyRules, VaultStatusSchema, VaultIssuesSchema } = loaded;

const date = (iso) => iso.slice(0, 10);
const copy = (overrides = {}) => ({
  fingerprint: "aeb1 80f7 9014 1a39 9680", verifiedAt: null, verifiedBy: null, reason: null, changed: false, dueAt: null,
  due: true, intervalDays: 90, ...overrides,
});

test("the offline copy reads as never verified, verified with its next date, changed, or waiting on the seal", () => {
  assert.equal(RootKeyRules.offlineCopyText(copy(), date), "Your offline copy was never verified against this key.");
  assert.equal(
    RootKeyRules.offlineCopyText(copy({ verifiedAt: "2026-10-01T10:00:00Z", verifiedBy: "max", dueAt: "2026-12-30T10:00:00Z", due: false }), date),
    "Verified 2026-10-01 by max; next due 2026-12-30.",
  );
  assert.equal(
    RootKeyRules.offlineCopyText(copy({ verifiedAt: "2026-07-01T10:00:00Z", verifiedBy: "max", dueAt: "2026-09-29T10:00:00Z" }), date),
    "Verified 2026-07-01 by max; due since 2026-09-29.",
  );
  assert.match(RootKeyRules.offlineCopyText(copy({ changed: true }), date), /changed since/);
  assert.equal(RootKeyRules.offlineCopyText(copy({ fingerprint: null, due: false }), date), "The fingerprint shows once the vault unseals.");
});

test("each file check finding has words, and an unknown one still reads", () => {
  for (const problem of ["missing", "unreadable", "exposed", "invalid", "mismatch"])
    assert.notEqual(RootKeyRules.problemText(problem), RootKeyRules.problemText("other"), problem);
  assert.match(RootKeyRules.problemText("exposed"), /chmod 0400/);
});

test("the owner's command takes the key on stdin, never as an argument, and prints what the console shows", () => {
  const command = RootKeyRules.command;
  assert.match(command, /wheelhouse\.vault\.root-key\.v1\\0/);
  assert.match(command, /< root\.key$/);
  assert.doesNotMatch(command, /sys\.argv/);
  const runbook = readFileSync(new URL("../../../../../../deployment/deployment.md", import.meta.url), "utf8");
  assert.ok(runbook.includes(command), "deployment.md § Checking the root key gives the owner the same command");
  let python = null;
  try {
    python = execFileSync("python3", ["--version"], { encoding: "utf8" });
  } catch {
    // No python3 here: the console's fingerprint scheme is pinned by the backend's known-answer test instead.
  }
  if (!python) return;
  const script = command.slice("python3 -c '".length, command.lastIndexOf("' < root.key"));
  const key = Buffer.from([...Array(32).keys()]).toString("base64") + "\n";
  assert.equal(execFileSync("python3", ["-c", script], { input: key, encoding: "utf8" }).trim(), "aeb1 80f7 9014 1a39 9680");
});

test("the vault status and issues decode the root key's checks, and an older console's still read", () => {
  const status = VaultStatusSchema.parse({
    mode: "builtin", state: "unsealed", reason: null, keyFile: "/data/deployments/vault/root.key", products: 1, secrets: 2,
    expiring: 0, expired: 0, offlineCopy: copy(), keyFileCheck: { at: "2026-10-26T10:00:00Z", problem: "exposed" },
  });
  assert.deepEqual([status.offlineCopy.fingerprint, status.offlineCopy.due, status.keyFileCheck.problem],
    ["aeb1 80f7 9014 1a39 9680", true, "exposed"]);
  const older = VaultStatusSchema.parse({
    mode: "builtin", state: "sealed", reason: "missing", keyFile: "/k", products: 0, secrets: 0, expiring: 0, expired: 0,
  });
  assert.deepEqual([older.offlineCopy, older.keyFileCheck], [null, null]);
  const issues = VaultIssuesSchema.parse({ state: "unsealed", reason: null, expiries: [], offlineCopy: copy(), keyFile: null });
  assert.deepEqual([issues.offlineCopy.due, issues.keyFile], [true, null]);
  assert.deepEqual(VaultIssuesSchema.parse({ state: "unsealed", expiries: [] }).offlineCopy, null);
});
