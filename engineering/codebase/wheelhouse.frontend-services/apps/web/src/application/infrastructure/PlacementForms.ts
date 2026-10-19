import { z } from "zod";
import {
  MAX_PLACED_SITES,
  PlacementPatterns,
  PlacementRules,
  type PlacementSite,
  type StartPlacementRequest,
} from "@/domain/infrastructure";
import type { Product } from "@/domain/products";
import type { Server } from "@/domain/servers";

/** A placement's editable fields. */
export interface PlacementFormModel {
  server: string;
  database: boolean;
  sites: PlacementSite[];
}

/** Validates a placement with the host's own rules: names, clashing nodes and at least one thing to place. */
export const placementFormSchema = z
  .object({
    server: z.string().min(1, "Choose a server."),
    database: z.boolean(),
    sites: z
      .array(
        z.object({
          site: z.string().trim().regex(PlacementPatterns.site, "Use the site's name from the manifest, such as app."),
          node: z.string().trim(),
        }),
      )
      .max(MAX_PLACED_SITES, `Place at most ${MAX_PLACED_SITES} sites at once.`),
  })
  .superRefine((values, context) => {
    values.sites.forEach((entry, index) => {
      const problem = PlacementRules.nodeProblem(entry.node.trim());
      if (problem) context.addIssue({ code: "custom", path: ["sites", index, "node"], message: problem });
    });
    for (const index of PlacementRules.clashingRows(values.sites))
      context.addIssue({ code: "custom", path: ["sites", index, "node"], message: "Each site needs its own node." });
    if (!values.database && !values.sites.length)
      context.addIssue({ code: "custom", path: ["sites"], message: "Choose databases, sites or both." });
  });

/** A new placement: the first server it can go on, both databases, and each declared site under its default node. */
export function placementFormOf(product: Pick<Product, "slug" | "environments">, servers: readonly Server[]): PlacementFormModel {
  return {
    server: PlacementRules.eligibleServers(servers)[0]?.slug ?? "",
    database: true,
    sites: PlacementRules.initialSites(product),
  };
}

/** The request a submitted dialog sends. */
export function placementRequestOf(values: PlacementFormModel): StartPlacementRequest {
  return {
    server: values.server,
    database: values.database,
    sites: values.sites.map((entry) => ({ site: entry.site.trim(), node: entry.node.trim() })),
  };
}
