import { computed, type Ref } from "vue";
import { createPersistentRef, localStorageStorageBroker } from "@wow-two-beta/fe/foundation/storage";
import { DeploymentEnvironment } from "@/domain/targets";

/** The `localStorage` keys the console's own view choices live under, beside `wheelhouse.theme`. */
export const PreferenceKeys = {
  showInactive: "wheelhouse.show-inactive",
  sidebarCollapsed: "wheelhouse.sidebar-collapsed",
  environment: "wheelhouse.environment",
  collapsedGroups: "wheelhouse.products-collapsed-workspaces",
  productsView: "wheelhouse.products-view",
  lastProduct: "wheelhouse.last-product",
  showAllAttempts: "wheelhouse.show-all-attempts",
  dismissedSelfNotices: "wheelhouse.self-deploy-dismissed",
} as const;

/** How many dismissed self-rollout notices are remembered, newest kept. @internal */
const MaxDismissedSelfNotices = 20;

/**
 * Re-encodes, once, a choice the console stored as a raw string before v0.10 (`dev`, `rows`, an empty environment):
 * the SDK broker reads JSON only, so the raw form would read as nothing remembered. A key already holding JSON —
 * `true`, a list, the last product — is left alone, so this runs on every load and changes nothing after the first.
 * @internal
 */
function adoptRawPreferences(): void {
  for (const key of Object.values(PreferenceKeys)) {
    let raw: string | null;
    try {
      raw = typeof window === "undefined" ? null : window.localStorage.getItem(key);
    } catch {
      return;
    }
    if (raw === null) continue;
    try {
      JSON.parse(raw);
    } catch {
      if (raw === "") localStorageStorageBroker.remove(key);
      else localStorageStorageBroker.write(key, raw);
    }
  }
}

adoptRawPreferences();

/** Accepts a stored boolean. @internal */
const decodeBoolean = (stored: unknown): boolean | undefined => (typeof stored === "boolean" ? stored : undefined);

/** Accepts a stored list, keeping its strings. @internal */
const decodeStrings = (stored: unknown): string[] | undefined =>
  Array.isArray(stored) ? stored.filter((item): item is string => typeof item === "string") : undefined;

const showInactive = createPersistentRef(PreferenceKeys.showInactive, false, { decode: decodeBoolean });
const sidebarCollapsed = createPersistentRef(PreferenceKeys.sidebarCollapsed, false, { decode: decodeBoolean });
const showAllAttempts = createPersistentRef(PreferenceKeys.showAllAttempts, false, { decode: decodeBoolean });

const environment = createPersistentRef<DeploymentEnvironment | null>(PreferenceKeys.environment, null, {
  decode: (stored) =>
    (Object.values(DeploymentEnvironment) as unknown[]).includes(stored) ? (stored as DeploymentEnvironment) : undefined,
});

const collapsedGroups = createPersistentRef<string[]>(PreferenceKeys.collapsedGroups, [], { decode: decodeStrings });

/**
 * Whether the deployment views show every attempt — the ones a later deploy replaced that day and the deploys that
 * changed nothing (refused, replaced in the queue, cancelled, a failed check before changes) — in the calendar, its day
 * panel, the list and the product tabs: the one "Show all attempts" switch, off by default, when each target shows its
 * latest deploy of a day. The measures count every attempt either way.
 */
export function useShowAllAttempts(): Ref<boolean> {
  return showAllAttempts;
}

/** Whether inactive products and services show everywhere — the one "Show inactive" switch. */
export function useShowInactive(): Ref<boolean> {
  return showInactive;
}

/** Whether the desktop sidebar is collapsed to its icon rail. */
export function useSidebarCollapsed(): Ref<boolean> {
  return sidebarCollapsed;
}

/** The environment the owner last chose on a product page; a pinned service opens in it. */
export function useLastEnvironment(): Ref<DeploymentEnvironment | null> {
  return environment;
}

/**
 * Which workspace groups on the Products page are collapsed, by workspace id. A group starts expanded, so one created
 * or imported later shows its products until the owner folds it.
 */
export function useGroupExpansion() {
  return {
    isExpanded: (id: string) => !collapsedGroups.value.includes(id),
    toggle: (id: string) => {
      collapsedGroups.value = collapsedGroups.value.includes(id)
        ? collapsedGroups.value.filter((item) => item !== id)
        : [...collapsedGroups.value, id];
    },
    expand: (id: string) => {
      if (collapsedGroups.value.includes(id)) collapsedGroups.value = collapsedGroups.value.filter((item) => item !== id);
    },
  };
}

/** How the Products page lays out workspaces and products from tablets up; phones always show cards. */
export const ProductsView = { Cards: "cards", Rows: "rows" } as const;
export type ProductsView = (typeof ProductsView)[keyof typeof ProductsView];

const productsView = createPersistentRef<ProductsView>(PreferenceKeys.productsView, ProductsView.Cards, {
  decode: (stored) => (stored === ProductsView.Rows || stored === ProductsView.Cards ? stored : undefined),
});

/** The Products page's Cards or Rows choice, remembered across visits. */
export function useProductsView(): Ref<ProductsView> {
  return productsView;
}

/** The product page last opened, and its tab: where the sidebar's Products returns to. */
export interface LastProduct {
  readonly slug: string;
  readonly tab: string | null;
}

const lastProduct = createPersistentRef<LastProduct | null>(PreferenceKeys.lastProduct, null, {
  decode: (stored) => {
    if (!stored || typeof stored !== "object") return undefined;
    const { slug, tab } = stored as Record<string, unknown>;
    return typeof slug === "string" && slug ? { slug, tab: typeof tab === "string" && tab ? tab : null } : undefined;
  },
});

/** The product page last opened; null before any, or after it was forgotten. */
export function useLastProduct(): Ref<LastProduct | null> {
  return lastProduct;
}

const storedSelfNotices = createPersistentRef<string[]>(PreferenceKeys.dismissedSelfNotices, [], {
  decode: (stored) => decodeStrings(stored)?.slice(-MaxDismissedSelfNotices),
});
const dismissedSelfNotices = computed<string[]>({
  get: () => storedSelfNotices.value,
  set: (value) => {
    storedSelfNotices.value = value.slice(-MaxDismissedSelfNotices);
  },
});

/** The self-rollouts whose notice the owner dismissed, by deployment id; the twenty newest are kept. */
export function useDismissedSelfNotices(): Ref<string[]> {
  return dismissedSelfNotices;
}
