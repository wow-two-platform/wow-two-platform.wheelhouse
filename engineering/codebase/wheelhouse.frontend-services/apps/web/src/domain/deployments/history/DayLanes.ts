import { Attention, DeployState } from '@/domain/common/StatusVocabulary';

import { DeploymentStates, type DeploymentStateReading } from '../DeploymentStatus';
import type { DeploymentItem } from './models/DeploymentItem';

/** The environments every day row has a lane for, in order; any other environment a day holds follows them. */
export const DayLaneEnvironments: readonly string[] = ['dev', 'prod'];

/** One deploy in a lane: a glyph in its state's icon and its attention's colour. */
export interface DayGlyph {
  readonly item: DeploymentItem;
  readonly reading: DeploymentStateReading;
  /** The release it carried, or its bundle when it names none. */
  readonly release: string;
  /** When it started, or was submitted when it never started. */
  readonly at: string;
  /** The server it went to, named only when the product runs that environment on more than one. */
  readonly server: string | null;
}

/** One environment of one product on the day: its deploys oldest first, so the last is what it runs now. */
export interface DayLane {
  readonly environment: string;
  readonly glyphs: readonly DayGlyph[];
  /** Red for act and amber for look from the lane's latest deploy; null when it needs neither. */
  readonly attention: typeof Attention.Act | typeof Attention.Look | null;
}

/** One product's row in the chosen day. */
export interface DayRow {
  /** The product's slug; the target's ID for a deploy whose product is gone. */
  readonly key: string;
  readonly name: string;
  /** Whether the row names a product the console can open. */
  readonly isProduct: boolean;
  /** One lane per environment of the day, in the day's column order; a lane with no deploy has no glyphs. */
  readonly lanes: readonly DayLane[];
  /** The loudest of its lanes' attention: the row's left-edge bar. */
  readonly attention: DayLane['attention'];
}

/** A target as the lanes need it: which product and environment it serves, on which server. */
export interface DayTarget {
  readonly id: string;
  readonly product: string;
  readonly environment: string;
  readonly serverId: string;
}

/** The order rows sort by attention: needs action, then a look, then the rest. @internal */
const RowRank = { act: 0, look: 1, none: 2 } as const;

/** Whether a deploy changed nothing. @internal */
const isUnchanged = (glyph: DayGlyph) => glyph.reading.state === DeployState.NotApplied;

/** Two digits. @internal */
const pad = (value: number) => String(value).padStart(2, '0');

/**
 * Pure rules for the chosen day as one compact row per product: a lane per environment, one glyph per deploy that day
 * in time order, so the last glyph in a lane is what that environment runs now. A row's left-edge bar follows its lanes'
 * latest deploys; rows needing action lead, then those worth a look, then the rest, by name within each.
 */
export const DayLanes = {
  /** The day's environment columns: dev and prod always, then any other environment its deploys reached. */
  environments(items: readonly DeploymentItem[]): string[] {
    const extra = [...new Set(items.map((item) => item.environment ?? 'other'))]
      .filter((environment) => !DayLaneEnvironments.includes(environment))
      .sort();
    return [...DayLaneEnvironments, ...extra];
  },

  /**
   * A lane's attention from its latest deploy that changed something: red for act, amber for look. A deploy that
   * changed nothing after it never hides that, though one needing a look (a failed check, an expired schedule) raises
   * a calm lane to amber; a lane holding only such deploys takes theirs.
   */
  attention(glyphs: readonly DayGlyph[]): DayLane['attention'] {
    let level: DayLane['attention'] = null;
    for (let index = glyphs.length - 1; index >= 0; index--) {
      const glyph = glyphs[index]!;
      const attention = glyph.reading.attention;
      if (isUnchanged(glyph)) {
        if (attention === Attention.Act) return Attention.Act;
        if (attention === Attention.Look) level = Attention.Look;
        continue;
      }
      if (attention === Attention.Act) return Attention.Act;
      return attention === Attention.Look ? Attention.Look : level;
    }
    return level;
  },

  /**
   * The day's rows: one per product (or per removed target), a lane per environment in `environments` order, each
   * lane's deploys oldest first. `targets` names the server of a deploy whose product runs that environment on more
   * than one; a lane that reached several servers that day names them too.
   */
  rows(
    items: readonly DeploymentItem[],
    names: ReadonlyMap<string, string>,
    targets: readonly DayTarget[] = [],
  ): DayRow[] {
    const environments = DayLanes.environments(items);
    const serverOf = new Map(targets.map((target) => [target.id, target.serverId] as const));
    const spread = new Map<string, Set<string>>();
    for (const target of targets) {
      const key = `${target.product}/${target.environment}`;
      spread.set(key, (spread.get(key) ?? new Set()).add(target.id));
    }
    const groups = new Map<string, { name: string; isProduct: boolean; items: DeploymentItem[] }>();
    for (const item of items) {
      const key = item.product ?? item.targetId ?? 'unknown';
      const group = groups.get(key) ?? {
        name: item.product ? (names.get(item.product) ?? item.product) : (item.targetId ?? 'Removed target'),
        isProduct: Boolean(item.product),
        items: [],
      };
      group.items.push(item);
      groups.set(key, group);
    }

    const rows = [...groups.entries()].map(([key, group]): DayRow => {
      const lanes = environments.map((environment): DayLane => {
        const own = group.items
          .filter((item) => (item.environment ?? 'other') === environment)
          .sort((left, right) => DayLanes.at(left).localeCompare(DayLanes.at(right)) || left.id.localeCompare(right.id));
        const reached = new Set(own.map((item) => item.targetId ?? ''));
        const isSpread = reached.size > 1 || (spread.get(`${key}/${environment}`)?.size ?? 0) > 1;
        const glyphs = own.map((item): DayGlyph => ({
          item,
          reading: DeploymentStates.of(item),
          release: item.release ?? item.bundleId ?? '—',
          at: DayLanes.at(item),
          server: isSpread && item.targetId ? (serverOf.get(item.targetId) ?? item.targetId) : null,
        }));
        return { environment, glyphs, attention: DayLanes.attention(glyphs) };
      });
      const attention = lanes.some((lane) => lane.attention === Attention.Act)
        ? Attention.Act
        : lanes.some((lane) => lane.attention === Attention.Look)
          ? Attention.Look
          : null;
      return { key, name: group.name, isProduct: group.isProduct, lanes, attention };
    });

    return rows.sort(
      (left, right) =>
        RowRank[left.attention ?? 'none'] - RowRank[right.attention ?? 'none'] ||
        left.name.localeCompare(right.name, undefined, { sensitivity: 'base' }) ||
        left.key.localeCompare(right.key),
    );
  },

  /** When a deploy happened on its day: when it started, or when it was submitted if it never started. */
  at(item: DeploymentItem): string {
    return item.startedAt ?? item.submittedAt;
  },

  /** A local wall-clock time, `14:05`. */
  time(instant: string): string {
    const date = new Date(instant);
    return `${pad(date.getHours())}:${pad(date.getMinutes())}`;
  },

  /** A glyph's one line: `v1.6.1 · 14:05 · Failed, rollback not allowed · sulton-max`, then `· on hel1` for a fleet. */
  hint(glyph: DayGlyph): string {
    const state = glyph.reading.detail ? `${glyph.reading.word}, ${glyph.reading.detail}` : glyph.reading.word;
    const parts = [glyph.release, DayLanes.time(glyph.at), state, glyph.item.actor ?? 'unknown actor'];
    if (glyph.server) parts.push(`on ${glyph.server}`);
    return parts.join(' · ');
  },
} as const;
