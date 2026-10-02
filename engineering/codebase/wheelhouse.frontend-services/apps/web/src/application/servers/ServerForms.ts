import { z } from "zod";
import { defaultMapFieldPath } from "@wow-two-beta/ui-vue/forms-engine";
import { InventoryRules } from "@/domain/common";
import { VpsProvider, type SaveServerRequest, type Server } from "@/domain/servers";

/** Represents a server's editable fields, its ingress flattened beside them; empty optional text means absent. */
export interface ServerFormModel {
  slug: string;
  name: string;
  provider: VpsProvider;
  host: string;
  region: string;
  sshUser: string;
  sshPort: number | null;
  scheme: "http" | "https";
  port: number | null;
  entryPoints: string[];
  privateEntryPoints: string[];
  certResolver: string;
  pattern: string;
  probe: string;
  privateProbe: string;
}

/** Validates what the form can judge alone; the host checks addresses and names and names the field it refuses. */
export const serverFormSchema = z.object({
  slug: z.string().trim().regex(InventoryRules.slug, "Start with a lowercase letter; use lowercase letters, digits and dashes."),
  name: z.string().trim().min(1, "Enter a name."),
  provider: z.enum(VpsProvider),
  host: z.string().trim().min(1, "Enter the host SSH connects to."),
  region: z.string().trim().min(1, "Enter a region."),
  sshUser: z.string().trim().min(1, "Enter the SSH user."),
  sshPort: z.number("Enter the SSH port.").int().min(1).max(65535),
  scheme: z.enum(["http", "https"]),
  port: z.number().int().min(1).max(65535).nullable(),
  entryPoints: z.array(z.string()),
  privateEntryPoints: z.array(z.string()),
  certResolver: z.string().trim(),
  pattern: z.string().trim(),
  probe: z.string().trim(),
  privateProbe: z.string().trim(),
});

/** Creates detached editing state from a server, or a Hetzner default. */
export function serverFormOf(server: Server | null): ServerFormModel {
  const ingress = server?.ingress;
  return {
    slug: server?.slug ?? "",
    name: server?.name ?? "",
    provider: server?.provider ?? VpsProvider.Hetzner,
    host: server?.host ?? "",
    region: server?.region ?? "",
    sshUser: server?.sshUser ?? "deploy",
    sshPort: server?.sshPort ?? 22,
    scheme: ingress?.scheme ?? "https",
    port: ingress?.port ?? null,
    entryPoints: [...(ingress?.entryPoints ?? ["websecure"])],
    privateEntryPoints: [...(ingress?.privateEntryPoints ?? [])],
    certResolver: ingress ? (ingress.certResolver ?? "") : "letsencrypt",
    pattern: ingress?.pattern ?? "",
    probe: ingress?.probe ?? "",
    privateProbe: ingress?.privateProbe ?? "",
  };
}

/** The request a submitted form sends; the slug goes only with a new server. */
export function serverRequestOf(values: ServerFormModel, isNew: boolean): SaveServerRequest {
  return {
    ...(isNew ? { slug: values.slug } : {}),
    name: values.name,
    provider: values.provider,
    host: values.host,
    region: values.region,
    sshUser: values.sshUser,
    sshPort: values.sshPort ?? 22,
    ingress: {
      scheme: values.scheme,
      port: values.port,
      entryPoints: values.entryPoints,
      privateEntryPoints: values.privateEntryPoints,
      certResolver: values.certResolver || null,
      pattern: values.pattern || null,
      probe: values.probe || null,
      privateProbe: values.privateProbe || null,
    },
  };
}

/** Maps a host error path onto the flat form; a tag list's per-entry refusal lands on the list. */
export function serverFieldPath(serverPath: string): string {
  return defaultMapFieldPath(serverPath)
    .replace(/^ingress\./, "")
    .replace(/^((?:privateE|e)ntryPoints)\[\d+\]$/, "$1");
}
