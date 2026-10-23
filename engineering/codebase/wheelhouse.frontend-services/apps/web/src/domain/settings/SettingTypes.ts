import type { DeploySettingValue, SecretFormat, SettingType } from "../deployments/models/DeploySettings";

const months = ["JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"];
const days = ["SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT"];
const macros = new Set(["@yearly", "@annually", "@monthly", "@weekly", "@daily", "@midnight", "@hourly"]);

/** The value of a cron token: a number, or a month or weekday name; -1 for neither. @internal */
function tokenValue(token: string, names: string[] | null): number {
  if (/^\d+$/.test(token)) return Number(token);
  const index = names ? names.indexOf(token.toUpperCase()) : -1;
  return index < 0 ? -1 : names === months ? index + 1 : index;
}

/** One comma-separated part of a cron field within its range. @internal */
function cronPart(part: string, [min, max]: [number, number], names: string[] | null, allowsLast: boolean): boolean {
  const inRange = (token: string, emptyOk = false) =>
    token === "" ? emptyOk : tokenValue(token, names) >= min && tokenValue(token, names) <= max;
  const step = part.split("/");
  if (!part || step.length > 2 || (step.length === 2 && !(/^\d+$/.test(step[1]!) && Number(step[1]) > 0))) return false;
  const head = step[0]!;
  if (head === "*" || head === "?") return head === "*" || step.length === 1;
  if (allowsLast && step.length === 1 && (head === "L" || head.endsWith("L") || head.includes("#") || head.endsWith("W"))) {
    const [base, nth] = head.replace(/[LW]+$/, "").split("#");
    return inRange(base ?? "", head === "L") && (nth === undefined || (/^\d+$/.test(nth) && Number(nth) >= 1 && Number(nth) <= 5));
  }
  const bounds = head.split("-");
  if (bounds.length > 2 || !bounds.every((bound) => inRange(bound))) return false;
  return bounds.length === 1 || tokenValue(bounds[0]!, names) <= tokenValue(bounds[1]!, names);
}

/** Reads a value the .NET way a date format names (`yyyy`, `MM`, `dd`, `HH`, `mm`, `ss`). @internal */
function parsesAsDate(value: string, format: string): boolean {
  const tokens = format.match(/yyyy|MM|dd|HH|mm|ss|[-./: T]/g);
  if (!tokens || tokens.join("") !== format) return false;
  let rest = value;
  const parts: Record<string, number> = {};
  for (const token of tokens) {
    const width = token === "yyyy" ? 4 : token.length === 2 ? 2 : 1;
    const piece = rest.slice(0, width);
    if (width === 1) {
      if (piece !== token) return false;
    } else {
      if (!/^\d+$/.test(piece) || piece.length !== width) return false;
      parts[token] = Number(piece);
    }
    rest = rest.slice(width);
  }
  if (rest) return false;
  const year = parts["yyyy"] ?? 2000;
  const month = parts["MM"] ?? 1;
  const day = parts["dd"] ?? 1;
  const date = new Date(Date.UTC(year, month - 1, day));
  return (
    month >= 1 && month <= 12 && date.getUTCDate() === day && date.getUTCMonth() === month - 1 &&
    (parts["HH"] ?? 0) <= 23 && (parts["mm"] ?? 0) <= 59 && (parts["ss"] ?? 0) <= 59
  );
}

/**
 * What each setting type accepts — the same rules Wheelhouse checks on save and the release generator checks declared
 * defaults with — and which key names read as secrets. The console checks a field before it saves, so a typo shows at
 * once; the server's answer still decides.
 */
export const SettingTypes = {
  /** The type names, in the order an editor offers them. */
  names: ["text", "number", "boolean", "choice", "url", "cron", "date", "email"] as const satisfies readonly SettingType[],

  /** The date format a date key takes when it names none. */
  defaultDateFormat: "yyyy-MM-dd",

  /** Why a value does not fit its type, or null; never quotes the value. */
  check(type: SettingType, value: string, format?: string | null, choices?: readonly string[] | null): string | null {
    switch (type) {
      case "number":
        return /^-?(0|[1-9]\d{0,30})(\.\d{1,30})?$/.test(value) ? null : "must be a number, such as 42 or 0.5";
      case "boolean":
        return value === "true" || value === "false" ? null : "must be true or false";
      case "choice":
        return choices?.includes(value) ? null : `must be one of ${(choices ?? []).slice(0, 10).join(", ")}`;
      case "url": {
        try {
          const url = new URL(value);
          return (url.protocol === "http:" || url.protocol === "https:") && url.hostname && !/\s/.test(value)
            ? null
            : "must be an absolute http or https link";
        } catch {
          return "must be an absolute http or https link";
        }
      }
      case "cron":
        return SettingTypes.isCron(value) ? null : "must be a cron expression of five or six fields, or @daily and the like";
      case "date": {
        const pattern = format || SettingTypes.defaultDateFormat;
        return parsesAsDate(value, pattern) ? null : `must be a date as ${pattern}`;
      }
      case "email":
        return /^[^@\s]{1,64}@[^@\s.]+(\.[^@\s.]+)+$/.test(value) ? null : "must be an email address";
      default:
        return null;
    }
  },

  /** Whether a value is a cron expression: five fields, six with seconds first, or a macro. */
  isCron(value: string): boolean {
    if (!value || value !== value.trim() || value.length > 200) return false;
    if (value.startsWith("@")) return macros.has(value);
    const fields = value.split(/ +/);
    if (fields.length !== 5 && fields.length !== 6) return false;
    const ranges: [number, number][] = [[0, 59], [0, 23], [1, 31], [1, 12], [0, 7]];
    if (fields.length === 6) ranges.unshift([0, 59]);
    return fields.every((field, index) => {
      const position = index - (fields.length - 5);
      const names = position === 3 ? months : position === 4 ? days : null;
      return field.split(",").every((part) => cronPart(part, ranges[index]!, names, position === 2 || position === 4));
    });
  },

  /** Whether a date format uses only the shared tokens, with a year, month and day. */
  isDateFormat(format: string): boolean {
    return format.length <= 40 && /^(yyyy|MM|dd|HH|mm|ss|[-./: T])+$/.test(format)
      && ["yyyy", "MM", "dd"].every((part) => format.includes(part));
  },

  /** Whether a key's name reads as a secret, as Wheelhouse and the runner guess it. */
  looksSecret(key: string): boolean {
    const sections = key.split(":");
    return /(password|passphrase|secret|token|apikey|key|pwd|credentials?|connectionstring|salt)$/i.test(sections.at(-1) ?? "")
      || (sections.length > 1 && sections[0]!.toLowerCase() === "connectionstrings");
  },

  /** The forms a generated secret value comes in, the default first. */
  secretFormats: ["hex", "base64url"] as const satisfies readonly SecretFormat[],

  /** Whether Wheelhouse can generate a secret's value of this type: text only, the type of an undeclared key. */
  canGenerate(type: SettingType | null | undefined): boolean {
    return !type || type === "text";
  },

  /** The save that has Wheelhouse generate a secret's value into the vault: no value travels, either way. */
  generated(key: string, format: SecretFormat = "hex"): DeploySettingValue {
    return { key, value: "", secret: true, generate: format };
  },

  /** Whether a key is a configuration path the runner accepts: sections joined by `:`. */
  isKey(key: string): boolean {
    return /^[A-Za-z0-9_.-]{1,100}(:[A-Za-z0-9_.-]{1,100}){0,9}$/.test(key);
  },

  /** What a field shows for the type as its placeholder. */
  placeholder(type: SettingType, format?: string | null): string {
    switch (type) {
      case "number":
        return "42";
      case "url":
        return "https://";
      case "cron":
        return "0 3 * * *";
      case "date":
        return format || SettingTypes.defaultDateFormat;
      case "email":
        return "name@example.com";
      default:
        return "";
    }
  },
};
