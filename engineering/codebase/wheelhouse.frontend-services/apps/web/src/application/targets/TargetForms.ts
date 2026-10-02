import { z } from "zod";
import { InventoryRules } from "@/domain/common";
import {
  DeploymentEnvironment,
  type InventoryTarget,
  type SaveTargetRequest,
  type SiteHost,
  type SmokeCheck,
  type TargetSetting,
} from "@/domain/targets";

/** Represents an environment's editable fields; its product is fixed by where the form opens. */
export interface TargetFormModel {
  slug: string;
  environment: DeploymentEnvironment;
  server: string;
  network: string;
  root: string;
  settings: TargetSetting[];
  smokeChecks: SmokeCheck[];
  sites: SiteHost[];
}

/** Validates what the form can judge alone; the host checks paths, hosts and duplicates and names the field. */
export const targetFormSchema = z.object({
  slug: z.string().trim().regex(InventoryRules.slug, "Start with a lowercase letter; use lowercase letters, digits and dashes."),
  environment: z.enum(DeploymentEnvironment),
  server: z.string().min(1, "Choose a server."),
  network: z.string().trim().min(1, "Enter a network."),
  root: z.string().trim().min(1, "Enter the releases folder."),
  settings: z.array(z.object({ service: z.string().trim(), path: z.string().trim() })),
  smokeChecks: z.array(z.object({ service: z.string().trim(), path: z.string().trim(), status: z.number().int() })),
  sites: z.array(z.object({ site: z.string().trim(), host: z.string().trim() })),
});

/** The slug a new environment of a product suggests. */
export function suggestedTargetSlug(product: string, environment: DeploymentEnvironment): string {
  return `${product}-${environment}`;
}

/** Creates detached editing state from a target, or a dev environment on the first server. */
export function targetFormOf(target: InventoryTarget | null, product: string, firstServer: string | null): TargetFormModel {
  return {
    slug: target?.slug ?? suggestedTargetSlug(product, DeploymentEnvironment.Dev),
    environment: target?.environment ?? DeploymentEnvironment.Dev,
    server: target?.server ?? firstServer ?? "",
    network: target?.network ?? "platform",
    root: target?.root ?? "/srv/wheelhouse",
    settings: target?.settings.map((setting) => ({ ...setting })) ?? [],
    smokeChecks: target?.smokeChecks.map((check) => ({ ...check })) ?? [],
    sites: target?.sites.map((site) => ({ ...site })) ?? [],
  };
}

/** The request a submitted form sends; the slug goes only with a new environment. */
export function targetRequestOf(values: TargetFormModel, product: string, isNew: boolean): SaveTargetRequest {
  return {
    ...(isNew ? { slug: values.slug } : {}),
    product,
    server: values.server,
    environment: values.environment,
    network: values.network,
    root: values.root,
    settings: values.settings,
    smokeChecks: values.smokeChecks,
    sites: values.sites,
  };
}
