import assert from "node:assert/strict";
import { after, test } from "node:test";
import { stop } from "esbuild";
import { bundleModule } from "./BundleModule.mjs";

// The first-run intro memory: an intro shows once, a hand-edited or older value reads as nothing seen, and storage that
// cannot remember never lets an intro nag on every visit.
const { createIntroMemory, IntroMemoryKey, memoryStorageBroker } = await bundleModule(`
  export { createIntroMemory, IntroMemoryKey } from './src/application/intros';
  export { memoryStorageBroker } from '@wow-two-beta/fe/foundation/storage';
`);
after(() => stop());

test("an intro is seen once marked, and the mark survives a new memory over the same storage", () => {
  const broker = memoryStorageBroker();
  const memory = createIntroMemory(broker);
  assert.equal(memory.hasSeen("deployments.calendar"), false);
  memory.markSeen("deployments.calendar");
  memory.markSeen("deployments.calendar");
  assert.equal(memory.hasSeen("deployments.calendar"), true);
  assert.equal(memory.hasSeen("catalog.workspaces"), false);
  assert.deepEqual(broker.read(IntroMemoryKey), { version: 1, seen: ["deployments.calendar"] });
  assert.equal(createIntroMemory(broker).hasSeen("deployments.calendar"), true);
});

test("a value in another shape reads as nothing seen and is replaced on the next mark", () => {
  const broker = memoryStorageBroker();
  broker.write(IntroMemoryKey, ["deployments.calendar"]);
  const memory = createIntroMemory(broker);
  assert.equal(memory.hasSeen("deployments.calendar"), false);
  broker.write(IntroMemoryKey, { version: 1, seen: ["a", 7, null] });
  assert.equal(memory.hasSeen("a"), true);
  memory.markSeen("b");
  assert.deepEqual(broker.read(IntroMemoryKey), { version: 1, seen: ["a", "b"] });
});

test("storage that cannot remember reads every intro as seen, so none returns on each visit", () => {
  const memory = createIntroMemory(memoryStorageBroker(), () => false);
  assert.equal(memory.hasSeen("deployments.calendar"), true);
});
