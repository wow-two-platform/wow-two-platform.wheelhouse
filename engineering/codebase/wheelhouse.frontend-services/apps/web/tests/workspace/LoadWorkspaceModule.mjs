import { readFile } from 'node:fs/promises';
import ts from 'typescript';

/** Loads a framework-independent TypeScript module through the project's existing compiler. */
export async function loadWorkspaceModule(name) {
  const source = await readFile(new URL(`../../src/application/workspace/${name}.ts`, import.meta.url), 'utf8');
  const { outputText } = ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022, verbatimModuleSyntax: true },
  });
  return import(`data:text/javascript;base64,${Buffer.from(outputText).toString('base64')}`);
}

/** Creates a catalog product for workspace tests. */
export function product(overrides = {}) {
  return { slug: 'foreverpin', name: 'ForeverPin', description: 'Styled QR codes.', lifecycle: 'building',
    repository: { name: 'sulton-max/10x-venture-forever-pin', url: 'https://github.com/sulton-max/10x-venture-forever-pin',
      defaultBranch: 'main' },
    homepage: null, identity: { source: 'record', commit: null, problem: null, services: [] },
    iconUrl: '/api/products/foreverpin/icon', environments: [], ...overrides };
}

/** Creates a literal runner target for workspace tests. */
export function target(overrides = {}) {
  return { id: 'foreverpin-rehearsal', product: 'foreverpin', environment: 'rehearsal',
    serverId: 'rehearsal', provider: 'Local', host: '127.0.0.1', ...overrides };
}

/** Creates one observed container for workspace tests. */
export function container(overrides = {}) {
  return { service: 'management', state: 'running', health: 'healthy', restarts: 0,
    startedAt: '2026-09-26T00:00:00Z', exitCode: null, cpuPercent: null,
    memoryBytes: null, memoryLimitBytes: null, ...overrides };
}
