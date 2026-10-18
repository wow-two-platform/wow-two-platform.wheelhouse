import { localStorageStorageBroker, type StorageBroker } from "@wow-two-beta/fe/foundation/storage";

/** The `localStorage` key the first-run intros the owner has seen live under, beside the console's view choices. */
export const IntroMemoryKey = "wheelhouse.intros";

/** The stored shape, stamped so a later shape can migrate it. @internal */
interface StoredIntros {
  readonly version: 1;
  readonly seen: readonly string[];
}

/** Which first-run intros the owner has been shown. */
export interface IntroMemory {
  /** Whether the intro has been shown; unreadable or blocked storage reads as shown, so nothing nags. */
  hasSeen(id: string): boolean;
  /** Records the intro as shown; blocked storage keeps it for this visit only. */
  markSeen(id: string): void;
}

/**
 * Remembers shown intros through a storage broker. The console has one owner, so the browser's storage is the per-user
 * store; a value written in an older shape or by hand reads as nothing seen. Within one visit the memory holds every
 * id it marked, so blocked storage still shows each intro once.
 */
export function createIntroMemory(broker: StorageBroker, isAvailable: () => boolean = () => true): IntroMemory {
  const shown = new Set<string>();

  function read(): string[] {
    const stored = broker.read<Partial<StoredIntros>>(IntroMemoryKey);
    if (!stored || stored.version !== 1 || !Array.isArray(stored.seen)) return [];
    return stored.seen.filter((id): id is string => typeof id === "string");
  }

  return {
    hasSeen: (id) => shown.has(id) || !isAvailable() || read().includes(id),
    markSeen: (id) => {
      shown.add(id);
      const seen = read();
      if (!seen.includes(id))
        broker.write<StoredIntros>(IntroMemoryKey, {
          version: 1,
          seen: [...seen, id],
        });
    },
  };
}

/** Whether the browser's storage can be written; a private or locked-down browser cannot remember. @internal */
function canStore(): boolean {
  try {
    const storage = globalThis.localStorage;
    if (!storage) return false;
    storage.setItem(`${IntroMemoryKey}.probe`, "1");
    storage.removeItem(`${IntroMemoryKey}.probe`);
    return true;
  } catch {
    return false;
  }
}

/** The console's intro memory, over the browser's storage. */
export const introMemory: IntroMemory = createIntroMemory(localStorageStorageBroker, canStore);
