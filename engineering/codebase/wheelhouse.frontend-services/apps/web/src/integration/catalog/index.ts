import { z } from "zod";
import type { Catalog, CatalogImportResult } from "@/domain/catalog";
import { requestData } from "@/integration/common";

const nullable = z.string().nullish().transform((value) => value ?? null);

const CatalogSchema = z.object({
  sources: z.array(
    z.object({
      repository: z.string(),
      path: z.string(),
      ref: z.string(),
      name: nullable,
      commit: nullable,
      error: nullable,
    }),
  ),
  workspaces: z.array(
    z.object({
      slug: z.string(),
      name: z.string(),
      description: z.string().nullish().transform((value) => value ?? ""),
      source: z.string(),
    }),
  ),
  entries: z.array(
    z.object({
      slug: z.string(),
      name: z.string(),
      source: z.string(),
      workspace: z.string(),
      kind: z.string(),
      deploy: z.string(),
      purpose: z.string().nullish().transform((value) => value ?? ""),
      repositories: z.array(z.string()),
      productId: nullable,
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

/** The catalog Wheelhouse merges from every hub repository; imports carry the `catalog` action. */
export const catalogApi = {
  getCatalog: (signal?: AbortSignal) => requestData<Catalog>("/api/catalog", CatalogSchema, { signal }),
  /** Adds the chosen entries as products, creating their workspaces as needed. */
  importEntries: (slugs: readonly string[], signal?: AbortSignal) =>
    requestData<CatalogImportResult>("/api/catalog/import", ImportResultSchema, {
      method: "POST", body: { slugs }, signal, action: "catalog",
    }),
};
