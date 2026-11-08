/**
 * The Files migration (v0.14, dev slice): the owner moves a dev target's legacy files into a Files host, one wave or a
 * set of sources at a time, in three steps. A plan counts files and bytes and writes nothing; a push uploads them into
 * an import on Files; a verify compares what Files holds with the sources. A push without an import opens one, and a
 * push that resumes or a verify names it. The job follows like a deploy: each source is a step whose detail reads
 * `N files · B bytes`, the owner can cancel it (it then reads cancelled, never failed), and the sources' results stay on
 * the job.
 */

import { Measures } from '@/domain/common/Measures';

/** What the migration does with the picked sources. */
export const FilesMigrationStep = {
  Plan: 'plan',
  Push: 'push',
  Verify: 'verify',
} as const;
export type FilesMigrationStep = (typeof FilesMigrationStep)[keyof typeof FilesMigrationStep];

/** One source a repository's manifest lists. */
export interface FilesMigrationSource {
  id: string;
  kind: string;
  wave: number | null;
  repository: string | null;
}

/** A Files target of the environment, with the https addresses its app site answers on (the host, then its aliases). */
export interface FilesMigrationFilesTarget {
  target: string;
  urls: string[];
}

/** What the migration can move and where: the manifests' sources and the environment's Files targets. */
export interface FilesMigrationManifest {
  tenant: string;
  sources: FilesMigrationSource[];
  filesTargets: FilesMigrationFilesTarget[];
}

/** How the mover counted one source; `skipped` are files the import already holds, `failed` ones that did not move. */
export interface FilesMigrationResult {
  files: number;
  bytes: number;
  skipped: number;
  failed: number;
}

/** What a files migration job kept: the import it worked on (none for a plan) and each finished source's result. */
export interface FilesMigrationRun {
  import: string | null;
  results: Record<string, FilesMigrationResult>;
}

/** Which sources a step takes: every one, one wave, or the ids picked. */
export type FilesMigrationScope =
  | { readonly kind: 'all' }
  | { readonly kind: 'wave'; readonly wave: number }
  | { readonly kind: 'sources'; readonly ids: readonly string[] };

/** What the owner asks for: a step over a scope, into a Files target's address; a verify or a resumed push names the import. */
export interface FilesMigrationAsk {
  readonly step: FilesMigrationStep;
  readonly scope: FilesMigrationScope;
  readonly filesTarget: string;
  readonly filesUrl: string;
  readonly import?: string | null;
}

/** The body `POST …/files-migrations` takes. */
export interface FilesMigrationBody {
  step: FilesMigrationStep;
  wave?: number;
  sources?: string[];
  filesTarget: string;
  filesUrl: string;
  import?: string;
}

/** Pure rules for the Files migration dialog and the job it starts. */
export const FilesMigrations = {
  /** The steps in the order the dialog offers them. */
  steps: [FilesMigrationStep.Plan, FilesMigrationStep.Push, FilesMigrationStep.Verify] as readonly FilesMigrationStep[],

  /** Whether the console offers the migration on an environment: dev only, never on Wheelhouse itself. */
  offers: (target: { readonly environment: string; readonly product: string } | null | undefined, selfProduct: string): boolean =>
    Boolean(target) && target!.environment === 'dev' && target!.product !== selfProduct,

  /** Whether a step asks for the owner's confirmation first: a push and a verify reach Files, a plan reads only. */
  confirms: (step: FilesMigrationStep): boolean => step !== FilesMigrationStep.Plan,

  /** Whether a step needs an import id: a verify always; a push resumes one when given. */
  needsImport: (step: FilesMigrationStep): boolean => step === FilesMigrationStep.Verify,

  /** A step's name, capitalised. */
  verb: (step: FilesMigrationStep): string => (step === 'plan' ? 'Plan' : step === 'push' ? 'Push' : 'Verify'),

  /** The waves the sources belong to, ascending. */
  waves: (sources: readonly FilesMigrationSource[]): number[] =>
    [...new Set(sources.flatMap((source) => (source.wave === null ? [] : [source.wave])))].sort((left, right) => left - right),

  /** The sources a scope takes, in manifest order. */
  pick: (sources: readonly FilesMigrationSource[], scope: FilesMigrationScope): FilesMigrationSource[] =>
    sources.filter((source) =>
      scope.kind === 'all' ? true : scope.kind === 'wave' ? source.wave === scope.wave : scope.ids.includes(source.id),
    ),

  /** The host of a Files address, as the confirmation names it. */
  host: (url: string): string => {
    try {
      return new URL(url).host;
    } catch {
      return url;
    }
  },

  /** The question a push or a verify asks: `Push 3 sources to Files at files.dev.ocharo.app?`. */
  question: (step: FilesMigrationStep, count: number, url: string): string =>
    `${FilesMigrations.verb(step)} ${count} ${count === 1 ? 'source' : 'sources'} ${step === 'verify' ? 'against' : 'to'} Files at ${FilesMigrations.host(url)}?`,

  /** What the step does, for its confirmation. */
  consequence: (step: FilesMigrationStep, target: string, url: string): string =>
    step === FilesMigrationStep.Push
      ? `${target}'s legacy files upload into an import on Files at ${FilesMigrations.host(url)}. The sources are read only, `
        + 'the services keep running, and a cancel stops the push between files.'
      : `${target}'s legacy files are compared with what the import holds on Files at ${FilesMigrations.host(url)}. `
        + 'Nothing is written; the services keep running.',

  /** The request body for a step. A plan never carries an import; a push carries one only when it resumes. */
  body: (ask: FilesMigrationAsk): FilesMigrationBody => {
    const imported = ask.import?.trim();
    return {
      step: ask.step,
      ...(ask.scope.kind === 'wave' ? { wave: ask.scope.wave } : {}),
      ...(ask.scope.kind === 'sources' ? { sources: [...ask.scope.ids] } : {}),
      filesTarget: ask.filesTarget,
      filesUrl: ask.filesUrl,
      ...(ask.step !== FilesMigrationStep.Plan && imported ? { import: imported } : {}),
    };
  },

  /** Whether the ask is whole: a Files target and address, a non-empty scope, and the import a verify needs. */
  isComplete: (ask: Partial<FilesMigrationAsk>, picked: number): boolean =>
    Boolean(ask.step && ask.filesTarget && ask.filesUrl) && picked > 0
    && (!ask.step || !FilesMigrations.needsImport(ask.step) || Boolean(ask.import?.trim())),

  /** Whether an import id is one the API accepts: letters, digits, `_` and `-`, up to 64. */
  isImport: (value: string): boolean => /^[A-Za-z0-9_-]{1,64}$/.test(value),

  /** The import of the newest files-migration job of a target that has one; items come newest first. */
  latestImport: (
    items: readonly {
      readonly targetId: string | null;
      readonly operation?: { readonly kind: string } | null;
      readonly filesMigration?: { readonly import: string | null } | null;
    }[],
    target: string,
  ): string | null =>
    items.find((item) => item.targetId === target && item.operation?.kind === 'files-migration' && item.filesMigration?.import)
      ?.filesMigration?.import ?? null,

  /** `12 files · 3.3 KB`, as a source's step detail and result read. */
  counts: (result: Pick<FilesMigrationResult, 'files' | 'bytes'>): string =>
    `${result.files} ${result.files === 1 ? 'file' : 'files'} · ${Measures.bytes(result.bytes)}`,

  /** A job's results in the order of the sources it ran, then any other. */
  rows: (run: FilesMigrationRun | null | undefined, order: readonly string[] = []): Array<{ source: string; result: FilesMigrationResult }> => {
    const results = run?.results ?? {};
    const named = order.filter((source) => source in results);
    const rest = Object.keys(results).filter((source) => !order.includes(source));
    return [...named, ...rest].map((source) => ({ source, result: results[source]! }));
  },

  /** The totals over a job's results. */
  total: (run: FilesMigrationRun | null | undefined): FilesMigrationResult =>
    Object.values(run?.results ?? {}).reduce(
      (sum, result) => ({
        files: sum.files + result.files,
        bytes: sum.bytes + result.bytes,
        skipped: sum.skipped + result.skipped,
        failed: sum.failed + result.failed,
      }),
      { files: 0, bytes: 0, skipped: 0, failed: 0 },
    ),
} as const;
