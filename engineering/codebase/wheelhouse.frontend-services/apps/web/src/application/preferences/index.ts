import { ref, watch, type Ref } from "vue";
import { DeploymentEnvironment } from "@/domain/targets";

/** The `localStorage` keys the console's own view choices live under, beside `wheelhouse.theme`. */
export const PreferenceKeys = {
  showInactive: "wheelhouse.show-inactive",
  sidebarCollapsed: "wheelhouse.sidebar-collapsed",
  environment: "wheelhouse.environment",
  collapsedWorkspaces: "wheelhouse.sidebar-collapsed-workspaces",
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

const collapsedWorkspaces = preference<string[]>(
  PreferenceKeys.collapsedWorkspaces,
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
 * Which sidebar workspaces are collapsed, by workspace id. A workspace starts expanded, so one created or imported later
 * shows its products until the owner folds it.
 */
export function useWorkspaceExpansion() {
  return {
    isExpanded: (id: string) => !collapsedWorkspaces.value.includes(id),
    toggle: (id: string) => {
      collapsedWorkspaces.value = collapsedWorkspaces.value.includes(id)
        ? collapsedWorkspaces.value.filter((item) => item !== id)
        : [...collapsedWorkspaces.value, id];
    },
  };
}
