import type { DeploySetting, DeploySettings, DeploySettingValue, SettingType } from "./models/DeploySettings";

/** One key the deploy dialog asks for, once per product environment whatever the services declaring it. */
export interface PromptedSetting {
  key: string;
  required: boolean;
  secret: boolean;
  /** The services that take it. */
  services: string[];
  /** The release's default, shown as the field's placeholder. */
  default: string | null;
  allowed: string[] | null;
  /** What its value must look like; text when the release types it not. */
  type?: SettingType;
  /** A date key's format. */
  format?: string | null;
  /** Whether its name reads as a secret, so a plain value needs a confirmation. */
  looksSecret?: boolean;
  /** Whether the release declares it a secret, or its name reads as one: it always goes to the vault. */
  declaredSecret?: boolean;
}

/** The keys the render derives, named once each, for the dialog's summary. */
export interface DerivedSetting {
  key: string;
  value: string | null;
  derived: NonNullable<DeploySetting["derived"]>;
}

const merge = (prompted: Map<string, PromptedSetting>, service: string, key: DeploySetting) => {
  const known = prompted.get(key.key);
  if (known) {
    known.services.push(service);
    known.required ||= key.required;
    known.secret ||= key.secret;
    return;
  }
  prompted.set(key.key, {
    key: key.key,
    required: key.required,
    secret: key.secret,
    services: [service],
    default: key.default,
    allowed: key.allowed,
    type: key.type ?? "text",
    format: key.format ?? null,
    looksSecret: key.looksSecret ?? false,
    declaredSecret: key.secret,
  });
};

/** Pure rules of the deploy dialog's settings step. */
export const DeploySettingsRules = {
  /** What the dialog asks for: every required key no layer sets on a service without a host file, and on a first
   *  deploy every optional key still at its default or unset, so the owner sees what the release lets them choose. */
  prompted(settings: DeploySettings | null | undefined): PromptedSetting[] {
    const prompted = new Map<string, PromptedSetting>();
    for (const service of settings?.services ?? []) {
      for (const key of service.keys) {
        const missing = key.required && key.source === "missing" && !service.hostFile;
        const optional = settings!.firstDeploy && !key.required && (key.source === "default" || key.source === "unset");
        if (missing || optional) merge(prompted, service.name, key);
      }
    }
    return [...prompted.values()].sort(
      (a, b) => Number(b.required) - Number(a.required) || a.key.localeCompare(b.key),
    );
  },

  /** The keys the render derives, once each: hosts, proxies, site URLs and the target's database. */
  derived(settings: DeploySettings | null | undefined): DerivedSetting[] {
    const derived = new Map<string, DerivedSetting>();
    for (const service of settings?.services ?? []) {
      for (const key of service.keys) {
        if (key.source === "derived" && key.derived && !derived.has(key.key))
          derived.set(key.key, { key: key.key, value: key.value, derived: key.derived });
      }
    }
    return [...derived.values()].sort((a, b) => a.key.localeCompare(b.key));
  },

  /** The required keys still without a value once the owner's entries count: each holds the deploy. */
  blocking(prompted: readonly PromptedSetting[], entered: Readonly<Record<string, string>>): string[] {
    return prompted.filter((item) => item.required && !(entered[item.key] ?? "").trim()).map((item) => item.key);
  },

  /** The values to save: every filled entry, as a secret where the owner kept it one; blank optional ones are left to
   *  their defaults. */
  values(
    prompted: readonly PromptedSetting[],
    entered: Readonly<Record<string, string>>,
    secret: Readonly<Record<string, boolean>>,
  ): DeploySettingValue[] {
    return prompted
      .filter((item) => (entered[item.key] ?? "").trim())
      .map((item) => ({ key: item.key, value: entered[item.key]!, secret: secret[item.key] ?? item.secret }));
  },

  /** A one-line account of where the derived values come from. */
  describe(item: DerivedSetting): string {
    switch (item.derived) {
      case "hosts":
        return item.value ?? "the hosts the service answers on, and localhost";
      case "sites":
        return item.value ?? "the site's address";
      case "proxies":
        return "the ingress on the target's network";
      default:
        return "the target's own database";
    }
  },
};
