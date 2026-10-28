import { StatusState } from '@/domain/common';

/**
 * What a target knows about its databases' migration sets (migrations spec M36, M38 stage 1): each set's level and newest
 * migration, read by the latest rollout's migrate step, a rollback gate, or the owner's Check now. The database's own
 * history stays the only history; this is the target's last reading of it.
 */

/** Where a set's reading came from. */
export const MigrationSource = {
  /** A rollout's migrate step. */
  Migrate: 'migrate',
  /** A rollback gate: whether an older release may run on the database. */
  Gate: 'gate',
  /** The owner's Check now. */
  Check: 'check',
} as const;
export type MigrationSource = (typeof MigrationSource)[keyof typeof MigrationSource];

/** One migration set as the target last read it. Fields the runner could not read are null. */
export interface MigrationSetReading {
  name: string;
  /** The database schema the set owns; null where the set names none. */
  schema?: string | null | undefined;
  /** The service whose image carries the set. */
  service: string;
  level?: number | null | undefined;
  newestLabel?: string | null | undefined;
  /** `expand` or `contract`. */
  newestKind?: string | null | undefined;
  /** The database against the release: `current`, `ahead-compatible`, `behind`, … */
  state?: string | null | undefined;
  /** The oldest release that may run on this database. */
  rollbackFloor?: string | null | undefined;
  /** The release the reading was taken with. */
  release?: string | null | undefined;
  /** The rollout that took it; null for a check. */
  job?: string | null | undefined;
  source: string;
  at: string;
}

/** A target's migration sets; absent until the target ran a migrate step. */
export interface TargetMigrations {
  updatedAt?: string | null | undefined;
  sets: MigrationSetReading[];
}

/** One migration a migrate verb applied. */
export interface AppliedMigration {
  label: string;
  kind?: string | null | undefined;
}

/** One set as one migrate item reported it. */
export interface MigrationSetResult {
  name: string;
  schema?: string | null | undefined;
  state?: string | null | undefined;
  levelBefore?: number | null | undefined;
  levelAfter?: number | null | undefined;
  applied: AppliedMigration[];
  rollbackFloor?: string | null | undefined;
}

/** One service's migrate item in a check: `current`, `refused`, `failed`, `busy` or `timed out`; `detail` is
 * operator-safe text. */
export interface MigrationCheckItem {
  service: string;
  verb?: string | null | undefined;
  release?: string | null | undefined;
  status: string;
  exitCode?: number | null | undefined;
  seconds?: number | null | undefined;
  detail?: string | null | undefined;
  sets: MigrationSetResult[];
}

/** What Check now answered: each migrate item of the release the target runs (none when it declares none), and the
 * target's sets with the check merged in. */
export interface MigrationCheck {
  targetId: string;
  project?: string | null | undefined;
  release?: string | null | undefined;
  checkedAt?: string | null | undefined;
  items: MigrationCheckItem[];
  migrations?: TargetMigrations | null | undefined;
}

/** The item statuses that say the database may serve the release. @internal */
const SETTLED: ReadonlySet<string> = new Set(['current', 'applied']);

/** The set states the release refuses to boot on. @internal */
const REFUSED: ReadonlySet<string> = new Set(['behind', 'ahead-incompatible', 'orphaned', 'drifted', 'owner-conflict']);

/** Each source's word beside the reading's time. @internal */
const SourceWords: Readonly<Record<MigrationSource, string>> = {
  migrate: 'Migrated',
  gate: 'Rollback gate',
  check: 'Checked',
};

/** Pure rules over a target's migration readings and checks. */
export const MigrationRules = {
  /** The sets to show; none while the target never ran a migrate step. */
  setsOf: (migrations: TargetMigrations | null | undefined): readonly MigrationSetReading[] => migrations?.sets ?? [],

  /** A set's name, then its schema where that differs: `identity`, `default · public`. */
  nameOf: (set: Pick<MigrationSetReading, 'name' | 'schema'>): string =>
    set.schema && set.schema !== set.name ? `${set.name} · ${set.schema}` : set.name,

  /** Whether the sets come from more than one service, so each row names its service. */
  spansServices: (sets: readonly MigrationSetReading[]): boolean => new Set(sets.map((set) => set.service)).size > 1,

  /**
   * A set's state in the console's vocabulary: current is healthy; a compatible ahead (an older release on a newer
   * database, as after a roll back) boots with a warning; what the release refuses to boot on — behind, an incompatible
   * ahead, orphans, drift, another owner's schema — is failed; anything else, or no reading, is unknown.
   */
  stateOf(state: string | null | undefined): StatusState {
    if (state === 'current') return StatusState.Healthy;
    if (state === 'ahead-compatible') return StatusState.Degraded;
    if (state && (REFUSED.has(state) || state.includes('incompatible'))) return StatusState.Failed;
    return StatusState.Unknown;
  },

  /** A set state's word: `ahead-compatible` reads `ahead, compatible`; no reading reads `no reading`. */
  wordOf: (state: string | null | undefined): string =>
    !state ? 'no reading' : state === 'owner-conflict' ? 'owner conflict' : state.replace('-', ', '),

  /** Where a reading came from, as the row says it before its time. */
  sourceOf: (source: string): string => SourceWords[source as MigrationSource] ?? source,

  /** The check's items that did not find the database ready for the release: refused, failed, busy or timed out. */
  failures: (check: MigrationCheck): readonly MigrationCheckItem[] => check.items.filter((item) => !SETTLED.has(item.status)),

  /** Whether an item found the database wrong for the release (refused or failed), rather than not getting an answer
   * (busy, timed out). */
  isBlocking: (item: MigrationCheckItem): boolean => item.status === 'refused' || item.status === 'failed',
} as const;
