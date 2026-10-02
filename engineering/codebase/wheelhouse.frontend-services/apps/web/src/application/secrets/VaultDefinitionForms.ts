import { z } from "zod";
import { InventoryRules } from "@/domain/common";
import type { SaveVaultRequest, VaultDefinition } from "@/domain/secrets";

/** Represents where Wheelhouse reaches a vault; the server it runs on is fixed by where the form opens. */
export interface VaultDefinitionFormModel {
  slug: string;
  name: string;
  url: string;
}

/** Validates what the form can judge alone; the host checks the endpoint's shape and names the field. */
export const vaultDefinitionFormSchema = z.object({
  slug: z.string().trim().regex(InventoryRules.slug, "Start with a lowercase letter; use lowercase letters, digits and dashes."),
  name: z.string().trim().min(1, "Enter a name."),
  url: z.string().trim().min(1, "Enter the endpoint."),
});

/** Creates detached editing state from a vault, or one named after its server. */
export function vaultDefinitionFormOf(vault: VaultDefinition | null, server: string): VaultDefinitionFormModel {
  return { slug: vault?.slug ?? `${server}-vault`, name: vault?.name ?? "", url: vault?.url ?? "http://vault:8080" };
}

/** The request a submitted form sends; the slug goes only with a new vault. */
export function vaultDefinitionRequestOf(values: VaultDefinitionFormModel, server: string, isNew: boolean): SaveVaultRequest {
  return { ...(isNew ? { slug: values.slug } : {}), name: values.name, server, url: values.url };
}
