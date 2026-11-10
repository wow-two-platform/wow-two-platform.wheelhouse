import type { DeploySetting, DeploySettings, DeploySettingSource, SettingGenerate, SettingLink } from "../deployments/models/DeploySettings";
import type { SettingChange } from "./models";
import { SettingTypes } from "./SettingTypes";

/** One key of an environment as the Settings and Secrets editors list it, once whatever the services that take it. */
export interface EnvironmentKey extends DeploySetting {
  /** The services that take it; empty for a key no service declares, which every service takes unless it is held. */
  services: string[];
  /** Whether its value changed since the target's last deploy rendered it. */
  unapplied: boolean;
}

const sourceOrder: Record<DeploySettingSource, number> = {
  missing: 0, host: 1, stored: 2, vault: 2, generate: 2, default: 3, linked: 3, unset: 4, derived: 5,
};

/** The keys every render derives, which take no value and no link. */
const derivedKeys = new Set(["allowedhosts", "deployment:trustedproxies"]);

/** A target as a link's picker reads it: its slug, product and environment. */
export interface LinkableTarget {
  id: string;
  product: string;
  environment: string;
}

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
          known.generate ??= key.generate;
          known.from ??= key.from;
          known.link ??= key.link;
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

  /** Whether a key takes another key's value — the release's `from`, or the owner's link to another product's key: it is
   *  read-only in every editor, and a save to it is refused. */
  isLinked(key: Pick<DeploySetting, "from"> & Partial<Pick<DeploySetting, "link">>): boolean {
    return Boolean(key.from || key.link);
  },

  /** Whether a key is one each deploy derives: `AllowedHosts`, `Deployment:TrustedProxies` or a `Sites:<site>:Url`. */
  isDerived(key: string): boolean {
    return derivedKeys.has(key.toLowerCase()) || /^Sites:[^:]+:Url$/i.test(key);
  },

  /** Whether the owner may link a key to another product's: neither derived nor linked already. */
  canLink(key: Pick<DeploySetting, "key" | "from" | "source"> & Partial<Pick<DeploySetting, "link">>): boolean {
    return !key.from && !key.link && key.source !== "derived" && !EnvironmentSettingsRules.isDerived(key.key);
  },

  /** A link's source in the editors' words. */
  linkLabel(link: SettingLink): string {
    return `linked to ${link.target} · ${link.key}`;
  },

  /** The targets a key of a plan may link to: another product's, in the plan's environment, by slug. */
  linkTargets(targets: readonly LinkableTarget[] | null | undefined, plan: Pick<DeploySettings, "product" | "environment">): string[] {
    return (targets ?? [])
      .filter((target) => target.product !== plan.product && target.environment === plan.environment)
      .map((target) => target.id)
      .sort((a, b) => a.localeCompare(b));
  },

  /** The keys of a source target's plan a link may take, matching the typed text: neither derived nor linked
   *  themselves, since links go one hop; secrets first, at most 50. */
  sourceKeys(plan: DeploySettings | null | undefined, typed: string): EnvironmentKey[] {
    const text = typed.trim().toLowerCase();
    return EnvironmentSettingsRules.keys(plan)
      .filter((key) => EnvironmentSettingsRules.canLink(key) && (!text || key.key.toLowerCase().includes(text)))
      .sort((a, b) => Number(EnvironmentSettingsRules.isSecret(b)) - Number(EnvironmentSettingsRules.isSecret(a)) || a.key.localeCompare(b.key))
      .slice(0, 50);
  },

  /** Why a link cannot be saved yet, or null; the server's answer still decides the rest (same product, environment,
   *  a second hop). */
  linkProblem(sourceTarget: string | null, sourceKey: string): string | null {
    const key = sourceKey.trim();
    if (!sourceTarget) return "Pick the target whose key it takes.";
    if (!key) return "Name the source key.";
    if (!SettingTypes.isKey(key)) return "A source key is a configuration path such as Section:Key.";
    if (EnvironmentSettingsRules.isDerived(key)) return `${key} is derived at each deploy of its own product; link to a key it holds.`;
    return null;
  },

  /** Whether Wheelhouse makes the key's value, so the owner regenerates it rather than typing one. */
  canRegenerate(key: Pick<DeploySetting, "generate" | "from"> & Partial<Pick<DeploySetting, "link">>): boolean {
    return Boolean(key.generate) && !key.from && !key.link;
  },

  /** How Wheelhouse makes a key's value, in a few words: `32 random bytes, base64url` or `command: …`. */
  generateLabel(generate: SettingGenerate | null | undefined): string | null {
    if (!generate) return null;
    if (generate.command?.length) return `command: ${generate.command.join(" ")}`;
    return `${generate.random ?? "?"} random bytes, ${generate.encoding ?? "base64url"}`;
  },

  /** Who takes a key, in the editors' words: the services that declare it, else every service, else no service yet
   *  when the next deploy holds it for a release that declares it. */
  takers(key: Pick<EnvironmentKey, "services"> & Partial<Pick<DeploySetting, "held">>): string {
    if (key.services.length) return key.services.join(", ");
    return key.held ? "held until a release declares it" : "every service";
  },

  /** The words a key's source reads as in the editors. */
  sourceLabel(key: Pick<DeploySetting, "source" | "default"> & Partial<Pick<DeploySetting, "from" | "link">>): string {
    if (key.link) return EnvironmentSettingsRules.linkLabel(key.link) + (key.source === "missing" ? ", not set yet" : "");
    switch (key.source) {
      case "linked":
        return `from ${key.from ?? "another key"}`;
      case "generate":
        return "generated at the next deploy";
      case "stored":
        return "set";
      case "vault":
        return "in vault";
      case "default":
        return `default ${key.default || "empty"}`;
      case "host":
        return "host file";
      case "missing":
        return key.from ? `from ${key.from}, not set yet` : "missing";
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
