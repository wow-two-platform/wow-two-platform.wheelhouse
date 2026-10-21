import { z } from "zod";
import {
  MAX_PLACED_SITES,
  PlacementRules,
  type PlacementPlan,
  type StartPlacementRequest,
} from "@/domain/infrastructure";
import type { Server } from "@/domain/servers";

/** A placement's choices: the server, whether to create the databases, and the private sites ticked. */
export interface PlacementFormModel {
  server: string;
  database: boolean;
  /** The private sites to place, by name; each gets its derived node. */
  sites: string[];
}

/** Requires a server and something to place; the dialog keeps Place off until both hold, so neither shows as an error. */
export const placementFormSchema = z
  .object({
    server: z.string().min(1, "Choose a server."),
    database: z.boolean(),
    sites: z.array(z.string()).max(MAX_PLACED_SITES, `Place at most ${MAX_PLACED_SITES} sites at once.`),
  })
  .superRefine((values, context) => {
    if (!PlacementForms.hasChoice(values))
      context.addIssue({ code: "custom", path: ["sites"], message: "Choose databases, sites or both." });
  });

/** A new placement: the first server it can go on, and everything the manifest calls for, ticked. */
export function placementFormOf(plan: PlacementPlan, servers: readonly Server[]): PlacementFormModel {
  return {
    server: PlacementRules.eligibleServers(servers)[0]?.slug ?? "",
    database: plan.databases.length > 0,
    sites: [...plan.sites],
  };
}

/**
 * The request a submitted dialog sends: only what the plan offers, each site under its node — `nodeOf` derives it the
 * way the dialog shows it.
 */
export function placementRequestOf(
  values: PlacementFormModel,
  plan: PlacementPlan,
  nodeOf: (site: string) => string,
): StartPlacementRequest {
  return {
    server: values.server,
    database: values.database && plan.databases.length > 0,
    sites: plan.sites.filter((site) => values.sites.includes(site)).map((site) => ({ site, node: nodeOf(site) })),
  };
}

/** Rules over the placement dialog's values. */
export const PlacementForms = {
  /** Whether anything is chosen: the databases, a site or both. */
  hasChoice: (values: Pick<PlacementFormModel, "database" | "sites">): boolean => values.database || values.sites.length > 0,

  /** The ticked sites after one is ticked or cleared, in the plan's order. */
  toggled: (plan: PlacementPlan, ticked: readonly string[], site: string, on: boolean): string[] =>
    plan.sites.filter((name) => (name === site ? on : ticked.includes(name))),
} as const;
