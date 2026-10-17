import { reactive } from "vue";

/** The `localStorage` key of the icon addresses the host answered not found. */
export const MissingIconsKey = "wheelhouse.missing-icons";

/** How long a refused address stays refused: a repository may gain its icon, and the host caches icons for an hour. */
const LifetimeMs = 12 * 60 * 60 * 1000;

/** Reads the remembered refusals that have not expired; blocked or malformed storage remembers none. @internal */
function load(): [string, number][] {
  try {
    const value: unknown = JSON.parse(globalThis.localStorage?.getItem(MissingIconsKey) ?? "{}");
    if (!value || typeof value !== "object") return [];
    const now = Date.now();
    return Object.entries(value).filter(
      (entry): entry is [string, number] => typeof entry[1] === "number" && entry[1] > now,
    );
  } catch {
    return [];
  }
}

/** Address → the time its refusal expires. Reactive, so every icon showing the address falls back at once. */
const missing = reactive(new Map<string, number>(load()));

/** Saves the refusals; storage that is blocked or full keeps them for this visit only. @internal */
function persist(): void {
  try {
    globalThis.localStorage?.setItem(MissingIconsKey, JSON.stringify(Object.fromEntries(missing)));
  } catch {
    /* The refusals still hold until the page reloads. */
  }
}

/**
 * The icon addresses the host answered not found, shared by every icon on the page and kept across visits for a while,
 * so a product without an icon is asked for it once rather than by every mark that shows it.
 */
export const IconAvailability = {
  /** Whether the host refused the address and the refusal has not expired. */
  isMissing: (url: string): boolean => {
    const until = missing.get(url);
    if (until === undefined) return false;
    if (until > Date.now()) return true;
    missing.delete(url);
    persist();
    return false;
  },

  /** Records that the host refused the address. */
  markMissing: (url: string): void => {
    missing.set(url, Date.now() + LifetimeMs);
    persist();
  },

  /** Forgets the refusals of one product's icons, or of every icon; the next mark asks again. */
  forget: (slug?: string): void => {
    const prefix = slug === undefined ? "" : `/api/products/${encodeURIComponent(slug)}/`;
    for (const url of [...missing.keys()]) if (url.startsWith(prefix)) missing.delete(url);
    persist();
  },
} as const;
