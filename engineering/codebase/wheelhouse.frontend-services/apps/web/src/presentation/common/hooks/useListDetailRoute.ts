import { computed, type ComputedRef } from "vue";
import { useRoute, useRouter, type LocationQueryRaw } from "vue-router";

/** A list-detail page's selection, held in URL query keys so the browser's back button closes the detail. */
export interface ListDetailRoute {
  /** True while any of the detail keys is in the URL; below the layout's breakpoint the detail replaces the list. */
  readonly isDetailOpen: ComputedRef<boolean>;
  /** Reads one query key's value, or null when the URL has none. */
  readonly read: (key: string) => string | null;
  /** Writes query keys (null removes one). Opening a detail adds a history entry; switching details replaces it. */
  readonly select: (values: Readonly<Record<string, string | null>>) => Promise<unknown>;
  /** Returns to the list: steps back when the list is the previous entry, otherwise navigates to it. */
  readonly back: () => void;
}

/** Drives a `ListDetailLayout` from the route's query; `keys` are the parameters that name the open detail. */
export function useListDetailRoute(keys: readonly string[]): ListDetailRoute {
  const route = useRoute();
  const router = useRouter();

  function read(key: string): string | null {
    const raw = route.query[key];
    const value = Array.isArray(raw) ? raw[0] : raw;
    return typeof value === "string" ? value : null;
  }

  const isDetailOpen = computed(() => keys.some((key) => read(key) !== null));

  /** The current query with `values` applied, keeping every unrelated parameter. @internal */
  function queryWith(values: Readonly<Record<string, string | null>>): LocationQueryRaw {
    const query: LocationQueryRaw = { ...route.query };
    for (const [key, value] of Object.entries(values)) {
      if (value === null) delete query[key];
      else query[key] = value;
    }
    return query;
  }

  function select(values: Readonly<Record<string, string | null>>): Promise<unknown> {
    const location = { path: route.path, query: queryWith(values) };
    return isDetailOpen.value ? router.replace(location) : router.push(location);
  }

  function back(): void {
    const list = { path: route.path, query: queryWith(Object.fromEntries(keys.map((key) => [key, null]))) };
    const previous = router.options.history.state.back;
    if (typeof previous === "string" && previous === router.resolve(list).fullPath) router.back();
    else void router.push(list);
  }

  return { isDetailOpen, read, select, back };
}
