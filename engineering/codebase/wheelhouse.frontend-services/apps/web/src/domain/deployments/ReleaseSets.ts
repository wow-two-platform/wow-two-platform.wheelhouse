/**
 * A product built from several repositories releases as a set (decision 70): one release of each repository that has
 * released; one that has not yet is left out until it does, and the set names it as not in it yet. Its label names each
 * as `<name>-<release>`, joined by `_` in repository order (`platform-v0.1.8_studio-v0.1.6`, or `platform-v0.1.8` while
 * only the primary repository has released), or `set-<12 hex>` when that would be too long. These rules read such a
 * label for people, as the runner's `release_sets.label_parts` does; every other label (`set-<12 hex>`, a repository's
 * own tag such as `v0.5.42`, a commit build) passes as is.
 */
export interface ReleasePart {
  /** The repository's name within the product. */
  readonly name: string;
  /** Its release tag (`v0.1.8`) or commit build (`sha-abcdef1`). */
  readonly release: string;
}

const PART =
  /^([a-z][a-z0-9-]*?)-(v(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?|sha-[0-9a-f]{7})$/;
const DIGEST = /^set-[0-9a-f]{12}$/;

/** A repository of the product that a set does not hold yet: it has not released, or names no service yet. */
export interface NotYetPart {
  /** The repository's name within the product. */
  readonly name: string | null;
  /** The repository, as `owner/name`. */
  readonly repository: string | null;
}

/**
 * Each repository's release in a set's joined label; null for any other label. A repository's own release tag
 * (`v0.1.8`) or commit build (`sha-…`) carries no name, so it never reads as a set.
 */
function parts(label: string | null | undefined): ReleasePart[] | null {
  if (!label || label.startsWith('set-')) return null;
  const pieces = label.split('_');
  const result: ReleasePart[] = [];
  for (const piece of pieces) {
    const match = PART.exec(piece);
    if (!match?.[1] || !match[2] || result.some((part) => part.name === match[1])) return null;
    result.push({ name: match[1], release: match[2] });
  }
  return result;
}

/** A release tag as people read it in a set: `v0.1.8` reads `0.1.8`; a commit build keeps its `sha-`. */
function version(release: string): string {
  return /^v\d/.test(release) ? release.slice(1) : release;
}

export const ReleaseSets = {
  parts,
  /** Whether a label is a release set's, joined or by digest. */
  isSet: (label: string | null | undefined): boolean => Boolean(label && (DIGEST.test(label) || parts(label))),
  /** A label in full for a list or a page: `platform v0.1.8 + studio v0.1.6`; any other label as it is. */
  text: (label: string | null | undefined): string => {
    const found = parts(label);
    return found ? found.map((part) => `${part.name} ${part.release}`).join(' + ') : (label ?? '');
  },
  /** The repositories a set does not hold yet, as people read them: `marketing`, `studio and marketing`; empty for none. */
  notYet: (items: readonly NotYetPart[] | null | undefined): string => {
    const names = (items ?? []).map((item) => item.name ?? item.repository ?? '').filter(Boolean);
    return names.length > 1 ? `${names.slice(0, -1).join(', ')} and ${names[names.length - 1]}` : (names[0] ?? '');
  },
  /** A label as a pill shows it: the first repository's version and how many more (`platform 0.1.8 +2`). */
  short: (label: string): string => {
    const found = parts(label);
    if (!found?.[0]) return label;
    const rest = found.length - 1;
    return `${found[0].name} ${version(found[0].release)}${rest > 0 ? ` +${rest}` : ''}`;
  },
};
