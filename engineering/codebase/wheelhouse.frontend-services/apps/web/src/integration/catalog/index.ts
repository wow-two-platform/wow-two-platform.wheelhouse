import { z } from "zod";
import type { Catalog, CatalogImportResult } from "@/domain/catalog";
import { requestData } from "@/integration/common";

const CatalogSchema = z.object({
  source: z.object({
    repository: z.string(),
    path: z.string(),
    ref: z.string(),
    commit: z.string().nullish().transform((value) => value ?? null),
  }),
  workspaces: z.array(
    z.object({
      slug: z.string(),
      name: z.string(),
      description: z.string().nullish().transform((value) => value ?? ""),
    }),
  ),
  entries: z.array(
    z.object({
      slug: z.string(),
      name: z.string(),
      workspace: z.string(),
      kind: z.string(),
      deploy: z.string(),
      purpose: z.string().nullish().transform((value) => value ?? ""),
      repositories: z.array(z.string()),
      productId: z.string().nullish().transform((value) => value ?? null),
    }),
  ),
});

/** An import reports counts; a list of what changed counts the same. */
const Changed = z.union([z.number().int().nonnegative(), z.array(z.unknown()).transform((items) => items.length)]);
const ImportResultSchema = z.object({
  createdWorkspaces: Changed,
  createdProducts: Changed,
  movedProducts: Changed,
  warnings: z.array(z.object({ slug: z.string(), message: z.string() })).default([]),
});

/** The wow2 catalog Wheelhouse reads from the workspace repository; imports carry the `catalog` action. */
export const catalogApi = {
  getCatalog: (signal?: AbortSignal) => requestData<Catalog>("/api/catalog", CatalogSchema, { signal }),
  /** Adds the chosen entries as products, creating their workspaces as needed. */
  importEntries: (slugs: readonly string[], signal?: AbortSignal) =>
    requestData<CatalogImportResult>("/api/catalog/import", ImportResultSchema, {
      method: "POST", body: { slugs }, signal, action: "catalog",
    }),
};
