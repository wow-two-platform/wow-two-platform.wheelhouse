import { ref, watch, type Ref } from "vue";
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

/** Reads a remembered value; storage that is blocked or full reads as nothing remembered. @internal */
function load(key: string): string | null {
  try {
    return globalThis.localStorage?.getItem(key) ?? null;
  } catch {
    return null;
  }
}

/** Remembers a value; storage that is blocked or full keeps it for this visit only. @internal */
function save(key: string, value: string): void {
  try {
    globalThis.localStorage?.setItem(key, value);
  } catch {
    /* The choice still holds until the page reloads. */
  }
}

/** One remembered view choice, shared by every component that reads it. @internal */
function preference<T>(key: string, read: (raw: string | null) => T, write: (value: T) => string): Ref<T> {
  const value = ref(read(load(key))) as Ref<T>;
  watch(value, (next) => save(key, write(next)));
  return value;
}

const showInactive = preference(
  PreferenceKeys.showInactive,
  (raw) => raw === "true",
  (value) => String(value),
);
const sidebarCollapsed = preference(
  PreferenceKeys.sidebarCollapsed,
  (raw) => raw === "true",
  (value) => String(value),
);
const environment = preference<DeploymentEnvironment | null>(
  PreferenceKeys.environment,
  (raw) => ((Object.values(DeploymentEnvironment) as string[]).includes(raw ?? "") ? (raw as DeploymentEnvironment) : null),
  (value) => value ?? "",
);

const collapsedGroups = preference<string[]>(
  PreferenceKeys.collapsedGroups,
  (raw) => {
    try {
      const value: unknown = JSON.parse(raw ?? "[]");
      return Array.isArray(value) ? value.filter((item): item is string => typeof item === "string") : [];
    } catch {
      return [];
    }
  },
  (value) => JSON.stringify(value),
);

const showAllAttempts = preference(
  PreferenceKeys.showAllAttempts,
  (raw) => raw === "true",
  (value) => String(value),
);

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

const productsView = preference<ProductsView>(
  PreferenceKeys.productsView,
  (raw) => (raw === ProductsView.Rows ? ProductsView.Rows : ProductsView.Cards),
  (value) => value,
);

/** The Products page's Cards or Rows choice, remembered across visits. */
export function useProductsView(): Ref<ProductsView> {
  return productsView;
}

/** The product page last opened, and its tab: where the sidebar's Products returns to. */
export interface LastProduct {
  readonly slug: string;
  readonly tab: string | null;
}

const lastProduct = preference<LastProduct | null>(
  PreferenceKeys.lastProduct,
  (raw) => {
    try {
      const value: unknown = JSON.parse(raw ?? "null");
      if (!value || typeof value !== "object") return null;
      const { slug, tab } = value as Record<string, unknown>;
      return typeof slug === "string" && slug ? { slug, tab: typeof tab === "string" && tab ? tab : null } : null;
    } catch {
      return null;
    }
  },
  (value) => JSON.stringify(value),
);

/** The product page last opened; null before any, or after it was forgotten. */
export function useLastProduct(): Ref<LastProduct | null> {
  return lastProduct;
}

const dismissedSelfNotices = preference<string[]>(
  PreferenceKeys.dismissedSelfNotices,
  (raw) => {
    try {
      const value: unknown = JSON.parse(raw ?? "[]");
      return Array.isArray(value) ? value.filter((item): item is string => typeof item === "string").slice(-20) : [];
    } catch {
      return [];
    }
  },
  (value) => JSON.stringify(value.slice(-20)),
);

/** The self-rollouts whose notice the owner dismissed, by deployment id; the twenty newest are kept. */
export function useDismissedSelfNotices(): Ref<string[]> {
  return dismissedSelfNotices;
}
