import type { DeploySetting, DeploySettings, DeploySettingSource } from "../deployments/models/DeploySettings";
import type { SettingChange } from "./models";

/** One key of an environment as the Settings and Secrets editors list it, once whatever the services that take it. */
export interface EnvironmentKey extends DeploySetting {
  /** The services that take it; empty for a key no service declares, which every service takes. */
  services: string[];
  /** Whether its value changed since the target's last deploy rendered it. */
  unapplied: boolean;
}

const sourceOrder: Record<DeploySettingSource, number> = {
  missing: 0, host: 1, stored: 2, vault: 2, default: 3, unset: 4, derived: 5,
};

/** Pure rules of an environment's Settings and Secrets editors. */
export const EnvironmentSettingsRules = {
  /** Every key once, declared ones with their services first, then the keys no service declares; required and missing
   *  first, then by key. */
  keys(plan: DeploySettings | null | undefined): EnvironmentKey[] {
    if (!plan) return [];
    const unapplied = new Set(plan.unapplied);
    const keys = new Map<string, EnvironmentKey>();
    for (const service of plan.services) {
      for (const key of service.keys) {
        const known = keys.get(key.key);
        if (known) {
          known.services.push(service.name);
          known.required ||= key.required;
          known.secret ||= key.secret;
          if (sourceOrder[key.source] < sourceOrder[known.source]) known.source = key.source;
          continue;
        }
        keys.set(key.key, { ...key, services: [service.name], unapplied: unapplied.has(key.key) });
      }
    }
    for (const key of plan.extra)
      if (!keys.has(key.key)) keys.set(key.key, { ...key, services: [], unapplied: unapplied.has(key.key) });
    return [...keys.values()].sort(
      (a, b) =>
        Number(b.required) - Number(a.required) ||
        sourceOrder[a.source] - sourceOrder[b.source] ||
        a.key.localeCompare(b.key),
    );
  },

  /** The plain settings an owner edits: neither secret nor derived. */
  settings(plan: DeploySettings | null | undefined): EnvironmentKey[] {
    return EnvironmentSettingsRules.keys(plan).filter((key) => !EnvironmentSettingsRules.isSecret(key) && key.source !== "derived");
  },

  /** The secrets: declared ones and any the vault holds. */
  secrets(plan: DeploySettings | null | undefined): EnvironmentKey[] {
    return EnvironmentSettingsRules.keys(plan).filter((key) => EnvironmentSettingsRules.isSecret(key) && key.source !== "derived");
  },

  /** The keys each deploy derives, for the editors' footnote. */
  derived(plan: DeploySettings | null | undefined): EnvironmentKey[] {
    return EnvironmentSettingsRules.keys(plan).filter((key) => key.source === "derived");
  },

  /** Whether a key's values belong in the vault: declared so, or already held there. */
  isSecret(key: Pick<DeploySetting, "secret" | "source">): boolean {
    return key.secret || key.source === "vault";
  },

  /** The words a key's source reads as in the editors. */
  sourceLabel(key: Pick<DeploySetting, "source" | "default">): string {
    switch (key.source) {
      case "stored":
        return "set";
      case "vault":
        return "in vault";
      case "default":
        return `default ${key.default || "empty"}`;
      case "host":
        return "host file";
      case "missing":
        return "missing";
      case "derived":
        return "derived";
      default:
        return "not set";
    }
  },

  /** The tone a secret's expiry shows in. */
  expiryTone(level: string | null | undefined): "danger" | "warning" | "neutral" {
    return level === "expired" ? "danger" : level === "7d" || level === "30d" ? "warning" : "neutral";
  },

  /** The words a secret's expiry shows as. */
  expiryLabel(level: string | null | undefined): string | null {
    return level === "expired" ? "expired" : level === "7d" ? "expires within 7 days" : level === "30d" ? "expires within 30 days" : null;
  },

  /** A failed deploy's hint: the settings it changed, named as the likely cause; null when it changed none. */
  hint(changes: readonly SettingChange[] | null | undefined, suspect: boolean): string | null {
    if (!changes?.length) return null;
    const names = changes.map((change) =>
      change.kind === "secret" && change.from !== null && change.to !== null
        ? `${change.key} v${change.from}→v${change.to}`
        : change.change === "added" ? `${change.key} (new)` : change.change === "removed" ? `${change.key} (removed)` : change.key,
    );
    const shown = names.slice(0, 4).join(", ") + (names.length > 4 ? ` and ${names.length - 4} more` : "");
    return suspect ? `Changed settings: ${shown} — likely cause` : `Changed settings: ${shown}`;
  },

  /** The earliest an apply may be scheduled for, and the latest (90 days on), as `datetime-local` values. */
  scheduleBounds(now: Date): { min: string; max: string } {
    const local = (date: Date) => new Date(date.getTime() - date.getTimezoneOffset() * 60_000).toISOString().slice(0, 16);
    return { min: local(new Date(now.getTime() + 5 * 60_000)), max: local(new Date(now.getTime() + 90 * 86_400_000)) };
  },
};
